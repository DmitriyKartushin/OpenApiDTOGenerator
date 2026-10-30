using System.Text.Json.Nodes;
using Json.Schema;
using OpenApiDtoGenerator.Core.Parsing;
using OpenApiDtoGenerator.Generators.JsonSchema;
using Shouldly;
using Xunit;

namespace OpenApiDtoGenerator.Integration.Tests;

/// <summary>
/// End-to-end: samples/complex/loan_application.yaml → JSON Schema → валидация реальных тел запросов.
///
/// Спецификация покрывает: внешние $ref (с вложенными ссылками внутри внешнего файла),
/// allOf, oneOf + discriminator, цикл Comment → replies → Comment, nullable (enum и $ref),
/// readOnly в required, exclusiveMinimum, additionalProperties (false и map), uniqueItems.
///
/// Тела запросов лежат в samples/complex/payloads:
///   valid_*.json   — должны проходить валидацию
///   invalid_*.json — должны отклоняться (имя файла описывает нарушение)
/// </summary>
public class ComplexRequestJsonSchemaTests
{
    private static string ComplexDir => Path.Combine(AppContext.BaseDirectory, "Fixtures", "complex");
    private static string SpecPath => Path.Combine(ComplexDir, "loan_application.yaml");
    private static string PayloadsDir => Path.Combine(ComplexDir, "payloads");

    private static readonly Lazy<Task<Dictionary<string, string>>> GeneratedFiles = new(GenerateAsync);

    public static TheoryData<string> ValidPayloads => PayloadFiles("valid_");
    public static TheoryData<string> InvalidPayloads => PayloadFiles("invalid_");

    // ═══════════════════════════════════════════════════════════════
    // Структура результата
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Generate_PostAndPut_ProducesSchemaPerOperationAndIndex()
    {
        var files = await GeneratedFiles.Value;

        files.Keys.ShouldBe(new[]
        {
            "createLoanApplication.Rq.schema.json",
            "replaceLoanApplication.Rq.schema.json",
            "post_loan-applications_id_comments.Rq.schema.json",
            "index.json"
        }, ignoreOrder: true);
    }

    [Fact]
    public async Task Generate_CreateSchema_ContainsAllReferencedDefsIncludingExternal()
    {
        var schema = await LoadSchemaAsync("createLoanApplication.Rq.schema.json");

        schema["$defs"]!.AsObject().Select(p => p.Key).ShouldBe(new[]
        {
            "Address", "Applicant", "Collateral", "Comment", "Currency", "Employment",
            "LoanApplication", "LoanProduct", "Money", "NoCollateral", "Person",
            "RealEstateCollateral", "VehicleCollateral"
        }, ignoreOrder: true);
    }

    [Fact]
    public async Task Generate_ReadOnlyApplicationId_NotRequired()
    {
        var schema = await LoadSchemaAsync("createLoanApplication.Rq.schema.json");

        var required = schema["$defs"]!["LoanApplication"]!["required"]!.AsArray()
            .Select(r => r!.GetValue<string>());
        required.ShouldNotContain("applicationId");
        required.ShouldContain("applicant");
    }

    // ═══════════════════════════════════════════════════════════════
    // Валидация тел запросов
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [MemberData(nameof(ValidPayloads))]
    public async Task Validate_ValidPayload_Passes(string payloadFile)
    {
        var result = await EvaluateAsync("createLoanApplication.Rq.schema.json", payloadFile);

        result.IsValid.ShouldBeTrue(FormatErrors(result));
    }

    [Theory]
    [MemberData(nameof(InvalidPayloads))]
    public async Task Validate_InvalidPayload_Fails(string payloadFile)
    {
        var result = await EvaluateAsync("createLoanApplication.Rq.schema.json", payloadFile);

        result.IsValid.ShouldBeFalse($"{payloadFile} должен быть отклонён схемой");
    }

    [Fact]
    public async Task Validate_InvalidFormats_RejectedOnlyWithRequireFormatValidation()
    {
        // В draft 2020-12 "format" по умолчанию — аннотация, не проверка.
        // Для проверки date/email/uuid валидатор нужно настроить явно.
        var schema = Json.Schema.JsonSchema.FromText((await GeneratedFiles.Value)["createLoanApplication.Rq.schema.json"]);
        var payload = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(PayloadsDir, "valid_full.json")))!;
        payload["applicant"]!["birthDate"] = "не-дата";
        payload["applicant"]!["email"] = "not-an-email";

        schema.Evaluate(payload).IsValid.ShouldBeTrue();
        schema.Evaluate(payload, new EvaluationOptions { RequireFormatValidation = true }).IsValid.ShouldBeFalse();
    }

    [Fact]
    public async Task Validate_PutSchema_AcceptsSamePayloadAsPost()
    {
        var result = await EvaluateAsync("replaceLoanApplication.Rq.schema.json", "valid_full.json");

        result.IsValid.ShouldBeTrue(FormatErrors(result));
    }

    [Fact]
    public async Task Validate_CommentSchema_RecursiveReplies()
    {
        var schema = await LoadSchemaAsync("post_loan-applications_id_comments.Rq.schema.json");
        var jsonSchema = Json.Schema.JsonSchema.FromText(schema.ToJsonString());

        Evaluate(jsonSchema, """{"text": "a", "replies": [{"text": "b", "replies": [{"text": "c"}]}]}""")
            .IsValid.ShouldBeTrue();
        Evaluate(jsonSchema, """{"text": "a", "replies": [{"text": "b", "replies": [{"replies": []}]}]}""")
            .IsValid.ShouldBeFalse();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<Dictionary<string, string>> GenerateAsync()
    {
        var parser = new YamlDocumentParser();
        var tree = parser.Parse(await File.ReadAllTextAsync(SpecPath));

        var files = await new JsonSchemaGenerator(parser: parser).GenerateAllAsync(
            tree, SpecPath, new JsonSchemaGeneratorOptions(HttpMethods: ["post", "put"]));

        return files.ToDictionary(f => f.FileName, f => f.Content);
    }

    private static async Task<JsonObject> LoadSchemaAsync(string fileName)
        => JsonNode.Parse((await GeneratedFiles.Value)[fileName])!.AsObject();

    private static async Task<EvaluationResults> EvaluateAsync(string schemaFile, string payloadFile)
    {
        var schema = Json.Schema.JsonSchema.FromText((await GeneratedFiles.Value)[schemaFile]);
        var payload = await File.ReadAllTextAsync(Path.Combine(PayloadsDir, payloadFile));
        return Evaluate(schema, payload);
    }

    private static EvaluationResults Evaluate(Json.Schema.JsonSchema schema, string json)
        => schema.Evaluate(JsonNode.Parse(json), new EvaluationOptions { OutputFormat = OutputFormat.List });

    private static string FormatErrors(EvaluationResults result)
        => string.Join(Environment.NewLine, result.Details
            .Where(d => d.HasErrors)
            .SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Key} — {e.Value}")));

    private static TheoryData<string> PayloadFiles(string prefix)
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.GetFiles(PayloadsDir, $"{prefix}*.json").Order())
            data.Add(Path.GetFileName(file));
        return data;
    }
}
