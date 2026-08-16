using OpenApiDtoGenerator.Core.Resolving;
using Shouldly;
using Xunit;

namespace OpenApiDtoGenerator.Core.Tests.Resolving;

/// <summary>
/// TDD: Шаг 1.3 — LocalFileLoader
///
/// Проверяем:
///   - успешное чтение существующего файла
///   - поддержку .yaml и .json
///   - ошибку на отсутствующем файле (FileNotFoundException)
///   - ошибку на директории вместо файла
///   - корректную работу с отменой (CancellationToken)
/// </summary>
public class LocalFileLoaderTests : IDisposable
{
    // Временная директория для тестовых файлов
    private readonly string _tempDir;

    public LocalFileLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"RefLoaderTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private LocalFileLoader CreateSut() => new();

    // ═══════════════════════════════════════════════════════════════
    // HAPPY PATH
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task LoadAsync_ExistingYamlFile_ReturnsContent()
    {
        // Arrange
        const string content = "openapi: 3.0.3\ninfo:\n  title: Test";
        var filePath = WriteFile("spec.yaml", content);
        var sut = CreateSut();

        // Act
        var result = await sut.LoadAsync(filePath, CancellationToken.None);

        // Assert
        result.ShouldBe(content);
    }

    [Fact]
    public async Task LoadAsync_ExistingJsonFile_ReturnsContent()
    {
        // Arrange
        const string content = """{ "openapi": "3.0.3" }""";
        var filePath = WriteFile("spec.json", content);
        var sut = CreateSut();

        // Act
        var result = await sut.LoadAsync(filePath, CancellationToken.None);

        // Assert
        result.ShouldBe(content);
    }

    [Fact]
    public async Task LoadAsync_FileWithUnicodeContent_ReturnsCorrectString()
    {
        // Arrange — кириллица в описании схемы
        const string content = "description: Персональные данные физического лица";
        var filePath = WriteFile("unicode.yaml", content);
        var sut = CreateSut();

        // Act
        var result = await sut.LoadAsync(filePath, CancellationToken.None);

        // Assert
        result.ShouldBe(content);
    }

    [Fact]
    public async Task LoadAsync_EmptyFile_ReturnsEmptyString()
    {
        // Arrange
        var filePath = WriteFile("empty.yaml", string.Empty);
        var sut = CreateSut();

        // Act
        var result = await sut.LoadAsync(filePath, CancellationToken.None);

        // Assert
        result.ShouldBe(string.Empty);
    }

    [Fact]
    public async Task LoadAsync_LargeFile_ReturnsFullContent()
    {
        // Arrange — файл ~100KB
        var bigContent = string.Join("\n", Enumerable.Range(0, 3000).Select(i => $"line_{i}: value_{i}"));
        var filePath = WriteFile("large.yaml", bigContent);
        var sut = CreateSut();

        // Act
        var result = await sut.LoadAsync(filePath, CancellationToken.None);

        // Assert
        result.ShouldBe(bigContent);
    }

    // ═══════════════════════════════════════════════════════════════
    // ERROR CASES
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task LoadAsync_NonExistentFile_ThrowsFileNotFoundException()
    {
        // Arrange
        var missingPath = Path.Combine(_tempDir, "does_not_exist.yaml");
        var sut = CreateSut();

        // Act & Assert
        await Should.ThrowAsync<FileNotFoundException>(
            () => sut.LoadAsync(missingPath, CancellationToken.None));
    }

    [Fact]
    public async Task LoadAsync_Directory_ThrowsInvalidOperationException()
    {
        // Arrange — передаём директорию вместо файла
        var sut = CreateSut();

        // Act & Assert
        await Should.ThrowAsync<InvalidOperationException>(
            () => sut.LoadAsync(_tempDir, CancellationToken.None));
    }

    [Fact]
    public async Task LoadAsync_NullPath_ThrowsArgumentException()
    {
        var sut = CreateSut();

        await Should.ThrowAsync<ArgumentException>(
            () => sut.LoadAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task LoadAsync_EmptyPath_ThrowsArgumentException()
    {
        var sut = CreateSut();

        await Should.ThrowAsync<ArgumentException>(
            () => sut.LoadAsync(string.Empty, CancellationToken.None));
    }

    // ═══════════════════════════════════════════════════════════════
    // CANCELLATION
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task LoadAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        // Arrange
        var filePath = WriteFile("cancel.yaml", "openapi: 3.0.3");
        var sut = CreateSut();
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // уже отменён

        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(
            () => sut.LoadAsync(filePath, cts.Token));
    }

    // ═══════════════════════════════════════════════════════════════
    // Helper
    // ═══════════════════════════════════════════════════════════════

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content, System.Text.Encoding.UTF8);
        return path;
    }
}
