using OpenApiDtoGenerator.Cli.Commands;
using OpenApiDtoGenerator.Cli.Config;
using OpenApiDtoGenerator.Generators.Masking;
using Shouldly;

namespace OpenApiDtoGenerator.Cli.Tests.Commands;

/// <summary>
/// End-to-end тест CLI: реальный openapi_example.yaml → сгенерированные .cs файлы.
///
/// Проверяет:
///   - что все файлы созданы в правильных папках
///   - что содержимое корректное (namespace, классы, атрибуты)
///   - что exit code = 0 при успехе
/// </summary>
public class GenerateCommandExecutorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _fixtureYaml;

    public GenerateCommandExecutorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"cli-gen-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _fixtureYaml = Path.Combine(AppContext.BaseDirectory, "Fixtures", "openapi_example.yaml");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ═══════════════════════════════════════════════════════════════
    // Полный пайплайн: DTO + Validators
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_FullPipeline_ExitCodeZero()
    {
        var opts = BasicOptions();
        var exitCode = await new GenerateCommandExecutor(_ => { }, _ => { }).ExecuteAsync(opts);
        exitCode.ShouldBe(0);
    }

    [Fact]
    public async Task ExecuteAsync_CreatesDtoDirectory()
    {
        await new GenerateCommandExecutor(_ => { }, _ => { }).ExecuteAsync(BasicOptions());
        Directory.Exists(Path.Combine(_tempDir, "Dto")).ShouldBeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_CreatesValidatorsDirectory()
    {
        await new GenerateCommandExecutor(_ => { }, _ => { }).ExecuteAsync(BasicOptions());
        Directory.Exists(Path.Combine(_tempDir, "Validators")).ShouldBeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_GeneratesPersonInfoDto()
    {
        await new GenerateCommandExecutor(_ => { }, _ => { }).ExecuteAsync(BasicOptions());

        var path = Path.Combine(_tempDir, "Dto", "PersonInfo.cs");
        File.Exists(path).ShouldBeTrue();

        var content = File.ReadAllText(path);
        content.ShouldContain("namespace MyApp.Dto");
        content.ShouldContain("public class PersonInfo");
        content.ShouldContain("LastName");
    }

    [Fact]
    public async Task ExecuteAsync_GeneratesPersonInfoValidator()
    {
        await new GenerateCommandExecutor(_ => { }, _ => { }).ExecuteAsync(BasicOptions());

        var path = Path.Combine(_tempDir, "Validators", "PersonInfoValidator.cs");
        File.Exists(path).ShouldBeTrue();

        var content = File.ReadAllText(path);
        content.ShouldContain("AbstractValidator<PersonInfo>");
        content.ShouldContain("RuleFor(x => x.LastName)");
    }

    [Fact]
    public async Task ExecuteAsync_GeneratesEnumFile()
    {
        await new GenerateCommandExecutor(_ => { }, _ => { }).ExecuteAsync(BasicOptions());

        var path = Path.Combine(_tempDir, "Dto", "DocumentType.cs");
        File.Exists(path).ShouldBeTrue();

        var content = File.ReadAllText(path);
        content.ShouldContain("public enum DocumentType");
        content.ShouldContain("PASSPORT");
    }

    // ═══════════════════════════════════════════════════════════════
    // С masking-конфигом
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_WithMasking_CreatesMaskingDirectory()
    {
        var opts = BasicOptions() with
        {
            MaskingConfig = BuildTestMaskingConfig()
        };

        await new GenerateCommandExecutor(_ => { }, _ => { }).ExecuteAsync(opts);

        Directory.Exists(Path.Combine(_tempDir, "Masking")).ShouldBeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WithMasking_GeneratesInfrastructure()
    {
        var opts = BasicOptions() with { MaskingConfig = BuildTestMaskingConfig() };

        await new GenerateCommandExecutor(_ => { }, _ => { }).ExecuteAsync(opts);

        File.Exists(Path.Combine(_tempDir, "Masking", "MaskStrategy.cs")).ShouldBeTrue();
        File.Exists(Path.Combine(_tempDir, "Masking", "MaskAttribute.cs")).ShouldBeTrue();
        File.Exists(Path.Combine(_tempDir, "Masking", "MaskingJsonOptions.cs")).ShouldBeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WithMasking_DtoContainsMaskAttribute()
    {
        var opts = BasicOptions() with { MaskingConfig = BuildTestMaskingConfig() };

        await new GenerateCommandExecutor(_ => { }, _ => { }).ExecuteAsync(opts);

        var passportPath = Path.Combine(_tempDir, "Dto", "PassportInfo.cs");
        File.Exists(passportPath).ShouldBeTrue();

        var content = File.ReadAllText(passportPath);
        content.ShouldContain("[Mask(");
        content.ShouldContain("partial class PassportInfo");
    }

    [Fact]
    public async Task ExecuteAsync_WithMasking_GeneratesPartialExtensionForMaskedDto()
    {
        var opts = BasicOptions() with { MaskingConfig = BuildTestMaskingConfig() };

        await new GenerateCommandExecutor(_ => { }, _ => { }).ExecuteAsync(opts);

        var extPath = Path.Combine(_tempDir, "Masking", "PassportInfo.Masking.cs");
        File.Exists(extPath).ShouldBeTrue();

        var content = File.ReadAllText(extPath);
        content.ShouldContain("ToMaskedJson()");
    }

    // ═══════════════════════════════════════════════════════════════
    // ONLY-режимы
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_DtoOnly_DoesNotCreateValidators()
    {
        var opts = BasicOptions() with { DtoOnly = true };
        await new GenerateCommandExecutor(_ => { }, _ => { }).ExecuteAsync(opts);

        Directory.Exists(Path.Combine(_tempDir, "Dto")).ShouldBeTrue();
        Directory.Exists(Path.Combine(_tempDir, "Validators")).ShouldBeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_ValidatorsOnly_DoesNotCreateDto()
    {
        var opts = BasicOptions() with { ValidatorsOnly = true };
        await new GenerateCommandExecutor(_ => { }, _ => { }).ExecuteAsync(opts);

        Directory.Exists(Path.Combine(_tempDir, "Validators")).ShouldBeTrue();
        Directory.Exists(Path.Combine(_tempDir, "Dto")).ShouldBeFalse();
    }

    // ═══════════════════════════════════════════════════════════════
    // JSON Schema
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_ByDefault_DoesNotCreateJsonSchemaDirectory()
    {
        await new GenerateCommandExecutor(_ => { }, _ => { }).ExecuteAsync(BasicOptions());

        Directory.Exists(Path.Combine(_tempDir, "JsonSchema")).ShouldBeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WithJsonSchema_WritesSchemaAndIndexToDisk()
    {
        var opts = BasicOptions() with { GenerateJsonSchema = true };

        var exitCode = await new GenerateCommandExecutor(_ => { }, _ => { }).ExecuteAsync(opts);

        exitCode.ShouldBe(0);
        Directory.Exists(Path.Combine(_tempDir, "Dto")).ShouldBeTrue();

        var schemaPath = Path.Combine(_tempDir, "JsonSchema", "createPerson.Rq.schema.json");
        File.Exists(schemaPath).ShouldBeTrue();
        File.Exists(Path.Combine(_tempDir, "JsonSchema", "index.json")).ShouldBeTrue();

        var schema = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(schemaPath))!;
        schema["$schema"]!.GetValue<string>().ShouldBe("https://json-schema.org/draft/2020-12/schema");
        schema["$ref"]!.GetValue<string>().ShouldBe("#/$defs/CreatePersonRequestBody");
        schema["$defs"]!["PassportInfo"]!["properties"]!["series"]!["pattern"]!
            .GetValue<string>().ShouldBe("^\\d{4}$");
    }

    [Fact]
    public async Task ExecuteAsync_JsonSchemaOnly_CreatesOnlyJsonSchema()
    {
        var opts = BasicOptions() with { JsonSchemaOnly = true };

        await new GenerateCommandExecutor(_ => { }, _ => { }).ExecuteAsync(opts);

        Directory.Exists(Path.Combine(_tempDir, "JsonSchema")).ShouldBeTrue();
        Directory.Exists(Path.Combine(_tempDir, "Dto")).ShouldBeFalse();
        Directory.Exists(Path.Combine(_tempDir, "Validators")).ShouldBeFalse();
    }

    // ═══════════════════════════════════════════════════════════════
    // Error cases
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_MissingInputFile_ReturnsNonZeroExitCode()
    {
        var opts = BasicOptions() with
        {
            InputPath = Path.Combine(_tempDir, "does-not-exist.yaml")
        };

        var exitCode = await new GenerateCommandExecutor(_ => { }, _ => { }).ExecuteAsync(opts);
        exitCode.ShouldNotBe(0);
    }

    // ─────────────────────────────────────────────────────────────────────────
    private GenerateOptions BasicOptions() => new()
    {
        InputPath = _fixtureYaml,
        OutputPath = _tempDir,
        DtoNamespace = "MyApp.Dto",
        ValidatorsNamespace = "MyApp.Validators",
        MaskingNamespace = "MyApp.Masking"
    };

    private static MaskingConfig BuildTestMaskingConfig() => new(new[]
    {
        new MaskingRule(new[] { "series", "number" }, MaskStrategy.Partial, VisibleStart: 2, VisibleEnd: 2),
        new MaskingRule(new[] { "lastName", "firstName" }, MaskStrategy.FirstChar),
        new MaskingRule(new[] { "issuedBy" }, MaskStrategy.Full)
    });
}
