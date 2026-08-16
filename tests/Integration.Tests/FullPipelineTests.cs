using OpenApiDtoGenerator.Core.Models;
using OpenApiDtoGenerator.Core.Parsing;
using OpenApiDtoGenerator.Core.Resolving;
using Shouldly;
using Xunit;

namespace OpenApiDtoGenerator.Integration.Tests;

/// <summary>
/// Полный интеграционный тест: реальный openapi_example.yaml → IR-граф.
///
/// Проверяет весь пайплайн end-to-end:
///   YamlDocumentParser → RefResolver → IRBuilder → SchemaNode граф
///
/// Используется реальный файл из Fixtures/openapi_example.yaml —
/// тот самый, что мы создали в начале беседы (PersonInfo, PassportInfo,
/// INN, SNILS, discriminator, requestBodies и т.д.)
/// </summary>
public class FullPipelineTests
{
    private static string FixturePath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "openapi_example.yaml");

    /// <summary>
    /// Создаёт полностью настроенный пайплайн (реальные компоненты, не моки).
    /// </summary>
    private static (IRBuilder builder, string documentKey, object? documentTree) CreatePipeline()
    {
        var yamlContent = File.ReadAllText(FixturePath);
        var yamlParser = new YamlDocumentParser();
        var documentTree = yamlParser.Parse(yamlContent);

        var loader = new LocalFileLoader();
        var cache = new DocumentCache();
        var refResolver = new RefResolver(loader, cache, yamlParser);
        var irBuilder = new IRBuilder(refResolver);

        return (irBuilder, FixturePath, documentTree);
    }

    // ═══════════════════════════════════════════════════════════════
    // Базовая загрузка и парсинг документа
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void YamlParser_LoadsRealFile_Successfully()
    {
        var yamlContent = File.ReadAllText(FixturePath);

        yamlContent.ShouldNotBeNullOrWhiteSpace();
        yamlContent.ShouldContain("openapi: 3.0.3");
    }

    [Fact]
    public void YamlParser_ParsesRealFile_ReturnsValidTree()
    {
        var yamlContent = File.ReadAllText(FixturePath);
        var parser = new YamlDocumentParser();

        var tree = parser.Parse(yamlContent);

        tree.ShouldBeOfType<Dictionary<string, object?>>();
        var dict = (Dictionary<string, object?>)tree!;
        dict.ShouldContainKey("openapi");
        dict.ShouldContainKey("components");
        dict.ShouldContainKey("paths");
    }

    [Fact]
    public void YamlParser_ParsesComponents_ContainsSchemas()
    {
        var yamlContent = File.ReadAllText(FixturePath);
        var parser = new YamlDocumentParser();
        var tree = (Dictionary<string, object?>)parser.Parse(yamlContent)!;

        var components = (Dictionary<string, object?>)tree["components"]!;
        components.ShouldContainKey("schemas");
        components.ShouldContainKey("requestBodies");

        var schemas = (Dictionary<string, object?>)components["schemas"]!;
        schemas.ShouldContainKey("PersonInfo");
        schemas.ShouldContainKey("PassportInfo");
        schemas.ShouldContainKey("INN");
        schemas.ShouldContainKey("SNILS");
    }

    // ═══════════════════════════════════════════════════════════════
    // Полный IRBuilder pipeline
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task FullPipeline_BuildsAllSchemas()
    {
        var (builder, documentKey, documentTree) = CreatePipeline();

        var schemas = await builder.BuildAsync(documentTree, documentKey);

        schemas.ShouldNotBeEmpty();
        schemas.Count.ShouldBeGreaterThan(10); // у нас 15+ схем в файле
    }

    [Fact]
    public async Task FullPipeline_ContainsCreatePersonRequestBody()
    {
        var (builder, documentKey, documentTree) = CreatePipeline();

        var schemas = await builder.BuildAsync(documentTree, documentKey);

        schemas.ShouldContainKey("CreatePersonRequestBody");
        var root = schemas["CreatePersonRequestBody"];
        root.Type.ShouldBe("object");
    }

    [Fact]
    public async Task FullPipeline_CreatePersonRequestBody_HasCorrectProperties()
    {
        var (builder, documentKey, documentTree) = CreatePipeline();

        var schemas = await builder.BuildAsync(documentTree, documentKey);
        var root = schemas["CreatePersonRequestBody"];

        var propertyNames = root.Properties.Select(p => p.Name).ToList();
        propertyNames.ShouldContain("personInfo");
        propertyNames.ShouldContain("inn");
        propertyNames.ShouldContain("snils");
        propertyNames.ShouldContain("metadata");
    }

    [Fact]
    public async Task FullPipeline_CreatePersonRequestBody_RequiredFieldsCorrect()
    {
        var (builder, documentKey, documentTree) = CreatePipeline();

        var schemas = await builder.BuildAsync(documentTree, documentKey);
        var root = schemas["CreatePersonRequestBody"];

        root.Properties.Single(p => p.Name == "personInfo").IsRequired.ShouldBeTrue();
        root.Properties.Single(p => p.Name == "inn").IsRequired.ShouldBeTrue();
        root.Properties.Single(p => p.Name == "snils").IsRequired.ShouldBeTrue();
        root.Properties.Single(p => p.Name == "metadata").IsRequired.ShouldBeFalse();
    }

    [Fact]
    public async Task FullPipeline_PersonInfo_HasNestedDocument()
    {
        var (builder, documentKey, documentTree) = CreatePipeline();

        var schemas = await builder.BuildAsync(documentTree, documentKey);
        var personInfo = schemas["PersonInfo"];

        personInfo.Properties.ShouldContain(p => p.Name == "document");
        personInfo.Properties.ShouldContain(p => p.Name == "lastName");
        personInfo.Properties.ShouldContain(p => p.Name == "firstName");
        personInfo.Properties.ShouldContain(p => p.Name == "middleName");
    }

    [Fact]
    public async Task FullPipeline_PassportInfo_HasValidationRules()
    {
        var (builder, documentKey, documentTree) = CreatePipeline();

        var schemas = await builder.BuildAsync(documentTree, documentKey);
        var passport = schemas["PassportInfo"];

        var series = passport.Properties.Single(p => p.Name == "series");
        series.Schema.Rules.Pattern.ShouldBe("^\\d{4}$");
        series.Schema.Rules.MinLength.ShouldBe(4);
        series.Schema.Rules.MaxLength.ShouldBe(4);

        var number = passport.Properties.Single(p => p.Name == "number");
        number.Schema.Rules.Pattern.ShouldBe("^\\d{6}$");
    }

    [Fact]
    public async Task FullPipeline_INN_HasCorrectPattern()
    {
        var (builder, documentKey, documentTree) = CreatePipeline();

        var schemas = await builder.BuildAsync(documentTree, documentKey);
        var inn = schemas["INN"];

        var valueProp = inn.Properties.Single(p => p.Name == "value");
        valueProp.Schema.Rules.Pattern.ShouldBe("^\\d{12}$");
        valueProp.Schema.Rules.MinLength.ShouldBe(12);
        valueProp.Schema.Rules.MaxLength.ShouldBe(12);
    }

    [Fact]
    public async Task FullPipeline_SNILS_HasCorrectPattern()
    {
        var (builder, documentKey, documentTree) = CreatePipeline();

        var schemas = await builder.BuildAsync(documentTree, documentKey);
        var snils = schemas["SNILS"];

        var valueProp = snils.Properties.Single(p => p.Name == "value");
        valueProp.Schema.Rules.Pattern.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task FullPipeline_DocumentType_IsEnum()
    {
        var (builder, documentKey, documentTree) = CreatePipeline();

        var schemas = await builder.BuildAsync(documentTree, documentKey);
        var docType = schemas["DocumentType"];

        docType.IsEnum.ShouldBeTrue();
        docType.EnumValues.ShouldContain("PASSPORT");
        docType.EnumValues.ShouldContain("FOREIGN_PASSPORT");
    }

    [Fact]
    public async Task FullPipeline_Gender_IsEnum()
    {
        var (builder, documentKey, documentTree) = CreatePipeline();

        var schemas = await builder.BuildAsync(documentTree, documentKey);
        var gender = schemas["Gender"];

        gender.IsEnum.ShouldBeTrue();
        gender.EnumValues.ShouldContain("MALE");
        gender.EnumValues.ShouldContain("FEMALE");
    }

    [Fact]
    public async Task FullPipeline_ValidationErrorResponse_HasAllOf()
    {
        var (builder, documentKey, documentTree) = CreatePipeline();

        var schemas = await builder.BuildAsync(documentTree, documentKey);
        var validationError = schemas["ValidationErrorResponse"];

        // allOf: [ErrorResponse, {доп. поля}]
        validationError.AllOf.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task FullPipeline_ForeignPassportInfo_HasRequiredFields()
    {
        var (builder, documentKey, documentTree) = CreatePipeline();

        var schemas = await builder.BuildAsync(documentTree, documentKey);
        var foreignPassport = schemas["ForeignPassportInfo"];

        var required = foreignPassport.Properties.Where(p => p.IsRequired).Select(p => p.Name).ToList();
        required.ShouldContain("series");
        required.ShouldContain("number");
        required.ShouldContain("expiryDate");
    }

    [Fact]
    public async Task FullPipeline_NoExceptions_ForFullDocument()
    {
        // Самый важный тест — весь документ должен разобраться без исключений
        var (builder, documentKey, documentTree) = CreatePipeline();

        await Should.NotThrowAsync(async () =>
        {
            await builder.BuildAsync(documentTree, documentKey);
        });
    }

    [Fact]
    public async Task FullPipeline_AllSchemaNamesPresent()
    {
        var (builder, documentKey, documentTree) = CreatePipeline();

        var schemas = await builder.BuildAsync(documentTree, documentKey);

        var expectedSchemas = new[]
        {
            "CreatePersonRequestBody",
            "PersonInfo",
            "IdentityDocument",
            "PassportInfo",
            "ForeignPassportInfo",
            "INN",
            "SNILS",
            "DocumentType",
            "Gender",
            "RequestMetadata",
            "PersonResponse",
            "ErrorResponse",
            "ValidationErrorResponse",
            "FieldViolation",
            "ConflictErrorResponse"
        };

        foreach (var expected in expectedSchemas)
        {
            schemas.ShouldContainKey(expected, $"Схема '{expected}' должна присутствовать в результате");
        }
    }
}
