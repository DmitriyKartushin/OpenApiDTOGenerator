using OpenApiDtoGenerator.Cli.Config;
using OpenApiDtoGenerator.Core.Parsing;
using OpenApiDtoGenerator.Core.Resolving;
using OpenApiDtoGenerator.Generators.Dto;
using OpenApiDtoGenerator.Generators.JsonSchema;
using OpenApiDtoGenerator.Generators.Masking;
using OpenApiDtoGenerator.Generators.Validators;

namespace OpenApiDtoGenerator.Cli.Commands;

/// <summary>
/// Основная команда генерации.
/// Выполняет весь пайплайн:
///   1. YAML/JSON парсинг
///   2. IR-построение (с резолвингом $ref)
///   3. Генерация DTO / Validators / Masking
///   4. Запись файлов на диск
/// </summary>
public sealed class GenerateCommandExecutor
{
    private readonly Action<string> _log;
    private readonly Action<string> _verboseLog;

    public GenerateCommandExecutor(Action<string>? log = null, Action<string>? verboseLog = null)
    {
        _log = log ?? Console.WriteLine;
        _verboseLog = verboseLog ?? (_ => { });
    }

    public async Task<int> ExecuteAsync(GenerateOptions options, CancellationToken ct = default)
    {
        try
        {
            _log($"📖  Reading OpenAPI spec: {options.InputPath}");
            var yamlContent = await File.ReadAllTextAsync(options.InputPath, ct);

            // ═══ 1. Parse YAML ═══════════════════════════════════════════
            var yamlParser = new YamlDocumentParser();
            var documentTree = yamlParser.Parse(yamlContent);
            _verboseLog($"   ✓ Parsed YAML successfully");

            // ═══ 2. Build IR ═════════════════════════════════════════════
            _log("🔧  Building intermediate representation (IR)...");
            var loader = new LocalFileLoader();
            var cache = new DocumentCache();
            var refResolver = new RefResolver(loader, cache, yamlParser);
            var irBuilder = new IRBuilder(refResolver);

            var schemas = await irBuilder.BuildAsync(documentTree, options.InputPath, ct);
            _log($"   ✓ {schemas.Count} schemas resolved");

            if (options.Verbose)
            {
                foreach (var name in schemas.Keys)
                    _verboseLog($"     • {name}");
            }

            // ═══ 3. Generate DTOs ════════════════════════════════════════
            if (options.ShouldGenerateDto)
            {
                _log("🎨  Generating DTO classes...");
                var dtoGenerator = new DtoGenerator();
                var dtoOptions = new DtoGeneratorOptions(
                    Namespace: options.DtoNamespace,
                    ClassPrefix: options.ClassPrefix,
                    ClassSuffix: options.ClassSuffix,
                    GeneratePartial: options.NeedsPartialDto,
                    MaskingConfig: options.MaskingConfig,
                    MaskingNamespace: options.MaskingConfig is not null ? options.MaskingNamespace : null);

                var dtoFiles = dtoGenerator.GenerateAll(schemas, dtoOptions);
                WriteFiles(dtoFiles, Path.Combine(options.OutputPath, "Dto"));
                _log($"   ✓ {dtoFiles.Count} DTO files written");
            }

            // ═══ 4. Generate Validators ══════════════════════════════════
            if (options.ShouldGenerateValidators)
            {
                _log("✅  Generating FluentValidation validators...");
                var validatorGenerator = new ValidatorGenerator();
                var validatorOptions = new DtoValidatorOptions(
                    Namespace: options.ValidatorsNamespace);

                var validatorFiles = validatorGenerator.GenerateAll(schemas, validatorOptions);
                WriteFiles(validatorFiles, Path.Combine(options.OutputPath, "Validators"));
                _log($"   ✓ {validatorFiles.Count} validator files written");
            }

            // ═══ 5. Generate Masking ═════════════════════════════════════
            if (options.ShouldGenerateMasking)
            {
                _log("🎭  Generating masking infrastructure and extensions...");
                var maskingGenerator = new MaskingGenerator();
                var maskingOptions = new MaskingGeneratorOptions(
                    Namespace: options.MaskingNamespace,
                    DtoNamespace: options.DtoNamespace,
                    Config: options.MaskingConfig!);

                var maskingFiles = maskingGenerator.GenerateAll(schemas, maskingOptions);
                WriteFiles(maskingFiles, Path.Combine(options.OutputPath, "Masking"));
                _log($"   ✓ {maskingFiles.Count} masking files written");
            }

            // ═══ 6. Generate JSON Schema ═════════════════════════════════
            if (options.ShouldGenerateJsonSchema)
            {
                _log("📐  Generating JSON Schema for request bodies...");
                var jsonSchemaGenerator = new JsonSchemaGenerator(loader, yamlParser);
                var jsonSchemaOptions = new JsonSchemaGeneratorOptions(
                    HttpMethods: options.JsonSchemaHttpMethods);

                var jsonSchemaFiles = await jsonSchemaGenerator.GenerateAllAsync(
                    documentTree, options.InputPath, jsonSchemaOptions, ct);
                WriteFiles(jsonSchemaFiles, Path.Combine(options.OutputPath, "JsonSchema"));
                _log($"   ✓ {jsonSchemaFiles.Count} JSON Schema files written");
            }

            _log("");
            _log($"✨  Generation complete. Output: {options.OutputPath}");
            return 0;
        }
        catch (FileNotFoundException ex)
        {
            _log($"❌  File not found: {ex.FileName}");
            return 2;
        }
        catch (FormatException ex)
        {
            _log($"❌  Format error: {ex.Message}");
            return 3;
        }
        catch (Exception ex)
        {
            _log($"❌  Unexpected error: {ex.Message}");
            if (options.Verbose)
                _log(ex.ToString());
            return 1;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────

    private void WriteFiles(IReadOnlyList<GeneratedFile> files, string directory)
    {
        Directory.CreateDirectory(directory);

        foreach (var file in files)
        {
            var fullPath = Path.Combine(directory, file.FileName);
            File.WriteAllText(fullPath, file.Content);
            _verboseLog($"     ✎ {fullPath}");
        }
    }
}
