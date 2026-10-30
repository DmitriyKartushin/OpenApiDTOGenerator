using System.CommandLine;
using OpenApiDtoGenerator.Cli.Commands;
using OpenApiDtoGenerator.Cli.Config;
using OpenApiDtoGenerator.Generators.Masking;

// ═══════════════════════════════════════════════════════════════════════════════
// OpenAPI DTO Generator — CLI entry point
//
// Usage:
//   openapi-dto-gen generate --input openapi.yaml --output ./Generated
//                            --namespace MyApp.Dto
//                            [--masking-config masking.yaml]
//                            [--json-schema] [--json-schema-methods post put] [--json-schema-responses]
//                            [--dto-only | --validators-only | --masking-only | --json-schema-only]
//                            [--verbose]
// ═══════════════════════════════════════════════════════════════════════════════

var rootCommand = new RootCommand("OpenAPI DTO Generator — DTOs, FluentValidation, JSON masking from OpenAPI YAML")
{
    BuildGenerateCommand()
};

return await rootCommand.InvokeAsync(args);

// ─────────────────────────────────────────────────────────────────────────────
// generate — основная команда
// ─────────────────────────────────────────────────────────────────────────────

static Command BuildGenerateCommand()
{
    var inputOption = new Option<FileInfo>(
        aliases: new[] { "--input", "-i" },
        description: "Path to OpenAPI YAML or JSON specification file.")
    { IsRequired = true };

    var outputOption = new Option<DirectoryInfo>(
        aliases: new[] { "--output", "-o" },
        description: "Output directory for generated files.",
        getDefaultValue: () => new DirectoryInfo("./Generated"));

    var namespaceOption = new Option<string>(
        aliases: new[] { "--namespace", "-n" },
        description: "Root namespace for generated DTOs.")
    { IsRequired = true };

    var validatorsNsOption = new Option<string?>(
        aliases: new[] { "--validators-namespace" },
        description: "Namespace for validators (default: <namespace>.Validators).",
        getDefaultValue: () => null);

    var maskingNsOption = new Option<string?>(
        aliases: new[] { "--masking-namespace" },
        description: "Namespace for masking infrastructure (default: <namespace>.Masking).",
        getDefaultValue: () => null);

    var prefixOption = new Option<string>(
        aliases: new[] { "--class-prefix" },
        description: "Prefix for class names (e.g. 'Dto' → DtoPersonInfo).",
        getDefaultValue: () => string.Empty);

    var suffixOption = new Option<string>(
        aliases: new[] { "--class-suffix" },
        description: "Suffix for class names (e.g. 'Dto' → PersonInfoDto).",
        getDefaultValue: () => string.Empty);

    var maskingConfigOption = new Option<FileInfo?>(
        aliases: new[] { "--masking-config" },
        description: "Path to YAML masking config file (enables masking generation).",
        getDefaultValue: () => null);

    var dtoOnlyOption = new Option<bool>(
        aliases: new[] { "--dto-only" },
        description: "Generate only DTO classes.");

    var validatorsOnlyOption = new Option<bool>(
        aliases: new[] { "--validators-only" },
        description: "Generate only validators.");

    var maskingOnlyOption = new Option<bool>(
        aliases: new[] { "--masking-only" },
        description: "Generate only masking infrastructure.");

    var jsonSchemaOption = new Option<bool>(
        aliases: new[] { "--json-schema" },
        description: "Also generate JSON Schema (draft 2020-12) for request bodies — for validating incoming requests.");

    var jsonSchemaOnlyOption = new Option<bool>(
        aliases: new[] { "--json-schema-only" },
        description: "Generate only JSON Schema for request bodies.");

    var jsonSchemaMethodsOption = new Option<string[]>(
        aliases: new[] { "--json-schema-methods" },
        description: "HTTP methods whose request bodies get a JSON Schema (default: post).",
        getDefaultValue: () => new[] { "post" })
    { AllowMultipleArgumentsPerToken = true };

    var jsonSchemaResponsesOption = new Option<bool>(
        aliases: new[] { "--json-schema-responses" },
        description: "Also generate JSON Schema for response bodies (*.Rs.<code>.schema.json). Use with --json-schema or --json-schema-only.");

    var verboseOption = new Option<bool>(
        aliases: new[] { "--verbose", "-v" },
        description: "Enable verbose output.");

    var generateCommand = new Command("generate", "Generate DTOs, validators, and masking from an OpenAPI spec.")
    {
        inputOption, outputOption, namespaceOption,
        validatorsNsOption, maskingNsOption,
        prefixOption, suffixOption,
        maskingConfigOption,
        dtoOnlyOption, validatorsOnlyOption, maskingOnlyOption,
        jsonSchemaOption, jsonSchemaOnlyOption, jsonSchemaMethodsOption, jsonSchemaResponsesOption,
        verboseOption
    };

    generateCommand.SetHandler(async context =>
    {
        var input        = context.ParseResult.GetValueForOption(inputOption)!;
        var output       = context.ParseResult.GetValueForOption(outputOption)!;
        var ns           = context.ParseResult.GetValueForOption(namespaceOption)!;
        var validatorsNs = context.ParseResult.GetValueForOption(validatorsNsOption);
        var maskingNs    = context.ParseResult.GetValueForOption(maskingNsOption);
        var prefix       = context.ParseResult.GetValueForOption(prefixOption)!;
        var suffix       = context.ParseResult.GetValueForOption(suffixOption)!;
        var maskingCfg   = context.ParseResult.GetValueForOption(maskingConfigOption);
        var dtoOnly      = context.ParseResult.GetValueForOption(dtoOnlyOption);
        var validatorsOnly = context.ParseResult.GetValueForOption(validatorsOnlyOption);
        var maskingOnly  = context.ParseResult.GetValueForOption(maskingOnlyOption);
        var verbose      = context.ParseResult.GetValueForOption(verboseOption);
        var jsonSchema   = context.ParseResult.GetValueForOption(jsonSchemaOption);
        var jsonSchemaOnly = context.ParseResult.GetValueForOption(jsonSchemaOnlyOption);
        var jsonSchemaMethods = context.ParseResult.GetValueForOption(jsonSchemaMethodsOption)!;
        var jsonSchemaResponses = context.ParseResult.GetValueForOption(jsonSchemaResponsesOption);

        // ── Валидация взаимоисключающих опций ─────────────────────────────
        var onlyFlags = new[] { dtoOnly, validatorsOnly, maskingOnly, jsonSchemaOnly }.Count(x => x);
        if (onlyFlags > 1)
        {
            Console.Error.WriteLine("❌  --dto-only, --validators-only, --masking-only, --json-schema-only are mutually exclusive.");
            context.ExitCode = 4;
            return;
        }

        if (maskingOnly && maskingCfg is null)
        {
            Console.Error.WriteLine("❌  --masking-only requires --masking-config.");
            context.ExitCode = 4;
            return;
        }

        // ── Загрузка masking-конфига (если задан) ─────────────────────────
        MaskingConfig? maskingConfig = null;
        if (maskingCfg is not null)
        {
            try
            {
                maskingConfig = new MaskingConfigLoader().LoadFromFile(maskingCfg.FullName);
                if (verbose)
                    Console.WriteLine($"   ✓ Masking config loaded: {maskingConfig.Rules.Count} rules");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"❌  Failed to load masking config: {ex.Message}");
                context.ExitCode = 3;
                return;
            }
        }

        // ── Собираем итоговый options-объект ──────────────────────────────
        var opts = new GenerateOptions
        {
            InputPath = input.FullName,
            OutputPath = output.FullName,
            DtoNamespace = ns,
            ValidatorsNamespace = validatorsNs ?? $"{ns}.Validators",
            MaskingNamespace    = maskingNs    ?? $"{ns}.Masking",
            ClassPrefix = prefix,
            ClassSuffix = suffix,
            MaskingConfigPath = maskingCfg?.FullName,
            MaskingConfig = maskingConfig,
            DtoOnly = dtoOnly,
            ValidatorsOnly = validatorsOnly,
            MaskingOnly = maskingOnly,
            GenerateJsonSchema = jsonSchema,
            JsonSchemaOnly = jsonSchemaOnly,
            JsonSchemaHttpMethods = jsonSchemaMethods,
            JsonSchemaResponses = jsonSchemaResponses,
            Verbose = verbose
        };

        // ── Запуск ─────────────────────────────────────────────────────────
        var executor = new GenerateCommandExecutor(
            log: Console.WriteLine,
            verboseLog: verbose ? Console.WriteLine : null);

        context.ExitCode = await executor.ExecuteAsync(opts);
    });

    return generateCommand;
}
