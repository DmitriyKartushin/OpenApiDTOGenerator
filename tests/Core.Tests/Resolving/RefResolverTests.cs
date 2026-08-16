using OpenApiDtoGenerator.Core.Resolving;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace OpenApiDtoGenerator.Core.Tests.Resolving;

/// <summary>
/// TDD: Шаг 1.8 — RefResolver (оркестратор)
///
/// Координирует:
///   1. RefParser — разбор $ref строки
///   2. DocumentLoader — загрузка документа
///   3. DocumentCache — кеширование
///   4. JsonPointerResolver — навигация по документу
///   5. CycleDetector — детектирование циклов
///
/// Сценарии:
///   - локальная ссылка — парсинг + JSON pointer
///   - относительный файл — нормализация пути, загрузка, кеширование
///   - дальний HTTP — загрузка, кеширование
///   - цикл — CycleDetector выбрасывает исключение
///   - несуществующий документ — FileNotFoundException от LoadAsync
///   - невалидный JSON Pointer — JsonPointerException
/// </summary>
public class RefResolverTests
{
    // ─────────────────────────────────────────────────────────────
    // Mock документов для тестирования
    // ─────────────────────────────────────────────────────────────

    private static Dictionary<string, object?> BuildSimpleDocument() => new()
    {
        ["openapi"] = "3.0.3",
        ["components"] = new Dictionary<string, object?>
        {
            ["schemas"] = new Dictionary<string, object?>
            {
                ["PersonInfo"] = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object?> { ["id"] = "string" }
                },
                ["PassportInfo"] = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["allOf"] = new List<object?>
                    {
                        new Dictionary<string, object?> { ["$ref"] = "#/components/schemas/DocumentBase" }
                    }
                },
                ["DocumentBase"] = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object?> { ["series"] = "string" }
                }
            }
        }
    };

    private static Dictionary<string, object?> BuildDocumentWithCycle() => new()
    {
        ["components"] = new Dictionary<string, object?>
        {
            ["schemas"] = new Dictionary<string, object?>
            {
                ["TreeNode"] = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["children"] = new Dictionary<string, object?>
                        {
                            ["type"] = "array",
                            ["items"] = new Dictionary<string, object?> { ["$ref"] = "#/components/schemas/TreeNode" }
                        }
                    }
                }
            }
        }
    };

    private RefResolver CreateSut(IDocumentLoader? loader = null)
    {
        loader ??= Substitute.For<IDocumentLoader>();
        return new RefResolver(loader, new DocumentCache());
    }

    // ═══════════════════════════════════════════════════════════════
    // LOCAL refs — #/...
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ResolveAsync_LocalRef_ParsesAndNavigates()
    {
        // Arrange
        var loader = Substitute.For<IDocumentLoader>();
        var docContent = YamlSerialize(BuildSimpleDocument());
        loader.LoadAsync("/home/user/spec.yaml", Arg.Any<CancellationToken>())
            .Returns(docContent);

        var sut = new RefResolver(loader, new DocumentCache());

        // Act — резолвим локальную ссылку
        var result = await sut.ResolveAsync(
            "#/components/schemas/PersonInfo",
            "/home/user/spec.yaml");

        // Assert
        result.ShouldNotBeNull();
        result.Name.ShouldBe("PersonInfo");
    }

    [Fact]
    public async Task ResolveAsync_LocalRef_DeepPath_NavigatesCorrectly()
    {
        var loader = Substitute.For<IDocumentLoader>();
        var docContent = YamlSerialize(BuildSimpleDocument());
        loader.LoadAsync("/home/user/spec.yaml", Arg.Any<CancellationToken>())
            .Returns(docContent);

        var sut = new RefResolver(loader, new DocumentCache());

        // Резолвим глубокий путь
        var result = await sut.ResolveAsync(
            "#/components/schemas/PersonInfo/properties/id",
            "/home/user/spec.yaml");

        result.ShouldNotBeNull();
    }

    [Fact]
    public async Task ResolveAsync_LocalRef_DoesNotCallLoader()
    {
        // Локальный ref — документ уже должен быть загружен ранее
        var loader = Substitute.For<IDocumentLoader>();
        var docContent = YamlSerialize(BuildSimpleDocument());
        loader.LoadAsync("/home/user/spec.yaml", Arg.Any<CancellationToken>())
            .Returns(docContent);

        var sut = new RefResolver(loader, new DocumentCache());

        // Сначала загружаем базовый документ
        _ = await sut.ResolveAsync("#/components/schemas/PersonInfo", "/home/user/spec.yaml");

        // Ресетим счётчик вызовов
        loader.ClearReceivedCalls();

        // Второй локальный ref в том же документе — не должен загружать заново
        _ = await sut.ResolveAsync("#/components/schemas/PassportInfo", "/home/user/spec.yaml");

        // Verify не вызывалась загрузка
        await loader.DidNotReceive().LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ═══════════════════════════════════════════════════════════════
    // RELATIVE FILE refs — ./... или ../...
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ResolveAsync_RelativeFileRef_NormalizesPathAndLoads()
    {
        // Arrange
        var loader = Substitute.For<IDocumentLoader>();
        var docContent = YamlSerialize(BuildSimpleDocument());

        // Ожидаем вызов с нормализованным абсолютным путём
        loader.LoadAsync("/home/user/schemas/person.yaml", Arg.Any<CancellationToken>())
            .Returns(docContent);

        var sut = new RefResolver(loader, new DocumentCache());

        // Act — резолвим относительную ссылку из /home/user/spec.yaml
        var result = await sut.ResolveAsync(
            "./schemas/person.yaml#/components/schemas/PersonInfo",
            "/home/user/spec.yaml");

        // Assert
        result.ShouldNotBeNull();
        result.Name.ShouldBe("PersonInfo");

        // Verify вызвана загрузка с нормализованным путём
        await loader.Received(1).LoadAsync(
            Arg.Is<string>(p => p == "/home/user/schemas/person.yaml"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_RelativeFileRef_WithParentDir_NormalizesCorrectly()
    {
        var loader = Substitute.For<IDocumentLoader>();
        var docContent = YamlSerialize(BuildSimpleDocument());

        loader.LoadAsync("/home/shared/common.yaml", Arg.Any<CancellationToken>())
            .Returns(docContent);

        var sut = new RefResolver(loader, new DocumentCache());

        // Из /home/user/schemas/passport.yaml → ../shared/common.yaml
        // должно быть /home/shared/common.yaml
        var result = await sut.ResolveAsync(
            "../shared/common.yaml#/components/schemas/PersonInfo",
            "/home/user/schemas/passport.yaml");

        result.ShouldNotBeNull();
    }

    // ═══════════════════════════════════════════════════════════════
    // HTTP refs — https://...
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ResolveAsync_HttpRef_LoadsAndCaches()
    {
        var loader = Substitute.For<IDocumentLoader>();
        var docContent = YamlSerialize(BuildSimpleDocument());

        loader.LoadAsync("https://api.example.com/specs/common.yaml", Arg.Any<CancellationToken>())
            .Returns(docContent);

        var sut = new RefResolver(loader, new DocumentCache());

        var result = await sut.ResolveAsync(
            "https://api.example.com/specs/common.yaml#/components/schemas/PersonInfo",
            "/home/user/spec.yaml");

        result.ShouldNotBeNull();

        // Verify вызвана загрузка
        await loader.Received(1).LoadAsync(
            Arg.Is<string>(u => u.Contains("api.example.com")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_HttpRef_CachesPreviousLoad()
    {
        var loader = Substitute.For<IDocumentLoader>();
        var docContent = YamlSerialize(BuildSimpleDocument());

        loader.LoadAsync("https://api.example.com/specs/common.yaml", Arg.Any<CancellationToken>())
            .Returns(docContent);

        var sut = new RefResolver(loader, new DocumentCache());

        // Первый запрос
        _ = await sut.ResolveAsync(
            "https://api.example.com/specs/common.yaml#/components/schemas/PersonInfo",
            "/home/user/spec.yaml");

        // Второй запрос к тому же URI — должен использовать кеш
        _ = await sut.ResolveAsync(
            "https://api.example.com/specs/common.yaml#/components/schemas/PassportInfo",
            "/home/user/spec.yaml");

        // LoadAsync вызвана только один раз (второй раз из кеша)
        await loader.Received(1).LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ═══════════════════════════════════════════════════════════════
    // CYCLE DETECTION
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ResolveAsync_SelfReferencingSchema_ThrowsCyclicReferenceException()
    {
        var loader = Substitute.For<IDocumentLoader>();
        var docContent = YamlSerialize(BuildDocumentWithCycle());

        loader.LoadAsync("/home/user/spec.yaml", Arg.Any<CancellationToken>())
            .Returns(docContent);

        var sut = new RefResolver(loader, new DocumentCache());

        // TreeNode → items → TreeNode (цикл)
        var ex = await Should.ThrowAsync<CyclicReferenceException>(
            () => sut.ResolveAsync(
                "#/components/schemas/TreeNode",
                "/home/user/spec.yaml"));

        ex.CyclicKey.ShouldContain("TreeNode");
    }

    // ═══════════════════════════════════════════════════════════════
    // ERROR CASES
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ResolveAsync_FileNotFound_ThrowsFileNotFoundException()
    {
        var loader = Substitute.For<IDocumentLoader>();
        loader.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws<FileNotFoundException>();

        var sut = new RefResolver(loader, new DocumentCache());

        await Should.ThrowAsync<FileNotFoundException>(
            () => sut.ResolveAsync(
                "./schemas/missing.yaml#/components/schemas/Foo",
                "/home/user/spec.yaml"));
    }

    [Fact]
    public async Task ResolveAsync_InvalidJsonPointer_ThrowsJsonPointerException()
    {
        var loader = Substitute.For<IDocumentLoader>();
        var docContent = YamlSerialize(BuildSimpleDocument());

        loader.LoadAsync("/home/user/spec.yaml", Arg.Any<CancellationToken>())
            .Returns(docContent);

        var sut = new RefResolver(loader, new DocumentCache());

        await Should.ThrowAsync<JsonPointerException>(
            () => sut.ResolveAsync(
                "#/components/schemas/NonExistent",
                "/home/user/spec.yaml"));
    }

    [Fact]
    public async Task ResolveAsync_NullRefString_ThrowsArgumentException()
    {
        var sut = CreateSut();

        await Should.ThrowAsync<ArgumentException>(
            () => sut.ResolveAsync(null!, "/home/user/spec.yaml"));
    }

    [Fact]
    public async Task ResolveAsync_NullCurrentDocumentKey_ThrowsArgumentException()
    {
        var sut = CreateSut();

        await Should.ThrowAsync<ArgumentException>(
            () => sut.ResolveAsync("#/components/schemas/Foo", null!));
    }

    [Fact]
    public async Task ResolveAsync_CancellationToken_Respected()
    {
        var loader = Substitute.For<IDocumentLoader>();
        loader.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new OperationCanceledException());

        var sut = new RefResolver(loader, new DocumentCache());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(
            () => sut.ResolveAsync(
                "./schemas/person.yaml#/components/schemas/Foo",
                "/home/user/spec.yaml",
                cts.Token));
    }

    // ═══════════════════════════════════════════════════════════════
    // HELPER
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Простая сериализация документа в YAML-like строку для тестов.
    /// В реальном коде парсится YamlDotNet — здесь просто для мока.
    /// </summary>
    private static string YamlSerialize(Dictionary<string, object?> doc)
    {
        // Для тестов достаточно JSON — YamlDotNet парсит и то и другое
        return System.Text.Json.JsonSerializer.Serialize(doc);
    }
}
