using System.Text.Json.Nodes;
using Json.Schema;
using OpenApiDtoGenerator.Core.Parsing;
using OpenApiDtoGenerator.Core.Resolving;
using OpenApiDtoGenerator.Generators.JsonSchema;
using Shouldly;

namespace OpenApiDtoGenerator.Generators.Tests.JsonSchema;

/// <summary>
/// JsonSchemaGenerator — OpenAPI 3.x → JSON Schema draft 2020-12 для тел запросов.
///
/// Проверяем:
///   - выбор операций (POST по умолчанию, JSON content types)
///   - перенос $ref в $defs (локальные, внешние, циклические)
///   - конвертацию OpenAPI-специфичных ключевых слов
///   - что результат реально валидирует JSON (через JsonSchema.Net)
/// </summary>
public class JsonSchemaGeneratorTests
{
    private const string DocumentKey = "/specs/api.yaml";

    private static readonly JsonSchemaGeneratorOptions DefaultOptions = new();

    // ═══════════════════════════════════════════════════════════════
    // Выбор операций и файлов
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GenerateAll_PostWithRequestBodyRef_GeneratesSchemaAndIndex()
    {
        var files = await GenerateAsync(PersonSpec);

        files.Keys.ShouldBe(new[] { "createPerson.Rq.schema.json", "index.json" }, ignoreOrder: true);

        var index = JsonNode.Parse(files["index.json"])!.AsArray();
        index.Count.ShouldBe(1);
        index[0]!["method"]!.GetValue<string>().ShouldBe("POST");
        index[0]!["path"]!.GetValue<string>().ShouldBe("/persons");
        index[0]!["contentType"]!.GetValue<string>().ShouldBe("application/json");
        index[0]!["required"]!.GetValue<bool>().ShouldBeTrue();
        index[0]!["schemaFile"]!.GetValue<string>().ShouldBe("createPerson.Rq.schema.json");
    }

    [Fact]
    public async Task GenerateAll_RootSchema_HasDialectAndRefToDefs()
    {
        var schema = await GenerateSchemaAsync(PersonSpec, "createPerson.Rq.schema.json");

        schema["$schema"]!.GetValue<string>().ShouldBe("https://json-schema.org/draft/2020-12/schema");
        schema["$ref"]!.GetValue<string>().ShouldBe("#/$defs/Person");
        schema["$defs"]!.AsObject().Select(p => p.Key)
            .ShouldBe(new[] { "Gender", "Person" }, ignoreOrder: true);
    }

    [Fact]
    public async Task GenerateAll_OnlyPostByDefault_IgnoresPutAndGet()
    {
        var files = await GenerateAsync(MultiMethodSpec);

        files.Keys.ShouldBe(new[] { "createItem.Rq.schema.json", "index.json" }, ignoreOrder: true);
    }

    [Fact]
    public async Task GenerateAll_CustomMethods_IncludesPut()
    {
        var files = await GenerateAsync(MultiMethodSpec, new JsonSchemaGeneratorOptions(HttpMethods: ["post", "PUT"]));

        files.Keys.ShouldBe(
            new[] { "createItem.Rq.schema.json", "replaceItem.Rq.schema.json", "index.json" },
            ignoreOrder: true);
    }

    [Fact]
    public async Task GenerateAll_NoOperationId_FileNameFromMethodAndPath()
    {
        const string yaml = """
            openapi: 3.0.3
            paths:
              /items/{id}/tags:
                post:
                  requestBody:
                    content:
                      application/json:
                        schema:
                          type: object
            """;

        var files = await GenerateAsync(yaml);

        files.ShouldContainKey("post_items_id_tags.Rq.schema.json");
    }

    [Fact]
    public async Task GenerateAll_NonJsonContentTypes_AreSkipped()
    {
        const string yaml = """
            openapi: 3.0.3
            paths:
              /upload:
                post:
                  operationId: upload
                  requestBody:
                    content:
                      multipart/form-data:
                        schema:
                          type: object
                      application/problem+json:
                        schema:
                          type: object
            """;

        var files = await GenerateAsync(yaml);

        files.Keys.ShouldBe(new[] { "upload.Rq.schema.json", "index.json" }, ignoreOrder: true);
        JsonNode.Parse(files["index.json"])![0]!["contentType"]!.GetValue<string>()
            .ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task GenerateAll_ResponsesDisabledByDefault_NoRsFiles()
    {
        var files = await GenerateAsync(PersonWithResponsesSpec);

        files.Keys.ShouldBe(new[] { "createPerson.Rq.schema.json", "index.json" }, ignoreOrder: true);
    }

    [Fact]
    public async Task GenerateAll_IncludeResponses_NamesFilesByStatusCodeAndIndexesThem()
    {
        var files = await GenerateAsync(PersonWithResponsesSpec, new JsonSchemaGeneratorOptions(IncludeResponses: true));

        files.Keys.ShouldBe(
            new[] { "createPerson.Rq.schema.json", "createPerson.Rs.201.schema.json", "createPerson.Rs.400.schema.json", "index.json" },
            ignoreOrder: true);

        var index = JsonNode.Parse(files["index.json"])!.AsArray();
        index.Count.ShouldBe(3);
        index[0]!["kind"]!.GetValue<string>().ShouldBe("Rq");
        index[0]!["statusCode"].ShouldBeNull();
        index[0]!["required"]!.GetValue<bool>().ShouldBeFalse();
        index[1]!["kind"]!.GetValue<string>().ShouldBe("Rs");
        index[1]!["statusCode"]!.GetValue<string>().ShouldBe("201");
        index[1]!["required"].ShouldBeNull();
        index[2]!["schemaFile"]!.GetValue<string>().ShouldBe("createPerson.Rs.400.schema.json");
    }

    [Fact]
    public async Task GenerateAll_IncludeResponses_ResolvesResponseRefAndKeepsReadOnlyRequired()
    {
        const string yaml = """
            openapi: 3.0.3
            paths:
              /persons/{id}:
                get:
                  operationId: getPerson
                  responses:
                    '200':
                      $ref: '#/components/responses/PersonOk'
            components:
              responses:
                PersonOk:
                  description: ok
                  content:
                    application/json:
                      schema:
                        type: object
                        required: [id]
                        properties:
                          id:
                            type: string
                            readOnly: true
            """;

        var files = await GenerateAsync(yaml, new JsonSchemaGeneratorOptions(HttpMethods: ["get"], IncludeResponses: true));

        files.Keys.ShouldBe(new[] { "getPerson.Rs.200.schema.json", "index.json" }, ignoreOrder: true);
        var schema = JsonNode.Parse(files["getPerson.Rs.200.schema.json"])!.AsObject();
        schema["required"]!.AsArray().Select(n => n!.GetValue<string>()).ShouldBe(new[] { "id" });
    }

    [Fact]
    public async Task GenerateAll_NoPostOperations_ReturnsNoFiles()
    {
        const string yaml = """
            openapi: 3.0.3
            paths:
              /items:
                get:
                  operationId: listItems
            """;

        var files = await GenerateAsync(yaml);

        files.ShouldBeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════
    // Конвертация ключевых слов OpenAPI → JSON Schema
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Convert_NullableString_BecomesTypeArrayWithNull()
    {
        var schema = await ConvertAsync("""
            type: string
            nullable: true
            """);

        schema["type"]!.AsArray().Select(t => t!.GetValue<string>()).ShouldBe(new[] { "string", "null" });
        schema.ContainsKey("nullable").ShouldBeFalse();
    }

    [Fact]
    public async Task Convert_NullableEnum_AddsNullToEnum()
    {
        var schema = await ConvertAsync("""
            type: string
            nullable: true
            enum: [A, B]
            """);

        Validate(schema, "null").ShouldBeTrue();
        Validate(schema, "\"A\"").ShouldBeTrue();
        Validate(schema, "\"C\"").ShouldBeFalse();
    }

    [Fact]
    public async Task Convert_NullableWithoutType_WrapsInAnyOf()
    {
        var schema = await ConvertAsync("""
            nullable: true
            allOf:
              - type: object
                required: [id]
            """);

        Validate(schema, "null").ShouldBeTrue();
        Validate(schema, """{"id": 1}""").ShouldBeTrue();
        Validate(schema, "{}").ShouldBeFalse();
    }

    [Fact]
    public async Task Convert_BooleanExclusiveMinimum_BecomesNumeric()
    {
        var schema = await ConvertAsync("""
            type: number
            minimum: 0
            exclusiveMinimum: true
            maximum: 10
            """);

        schema["exclusiveMinimum"]!.GetValue<int>().ShouldBe(0);
        schema.ContainsKey("minimum").ShouldBeFalse();
        schema["maximum"]!.GetValue<int>().ShouldBe(10);
        Validate(schema, "0").ShouldBeFalse();
        Validate(schema, "0.5").ShouldBeTrue();
    }

    [Fact]
    public async Task Convert_ReadOnlyProperty_ExcludedFromRequired()
    {
        var schema = await ConvertAsync("""
            type: object
            required: [id, name]
            properties:
              id:
                type: string
                readOnly: true
              name:
                type: string
            """);

        schema["required"]!.AsArray().Select(r => r!.GetValue<string>()).ShouldBe(new[] { "name" });
    }

    [Fact]
    public async Task Convert_ReadOnlyExclusionDisabled_KeepsRequired()
    {
        var schema = await ConvertAsync("""
            type: object
            required: [id]
            properties:
              id:
                type: string
                readOnly: true
            """, new JsonSchemaGeneratorOptions(ExcludeReadOnlyFromRequired: false));

        schema["required"]!.AsArray().Select(r => r!.GetValue<string>()).ShouldBe(new[] { "id" });
    }

    [Fact]
    public async Task Convert_OpenApiOnlyKeywords_AreRemoved()
    {
        var schema = await ConvertAsync("""
            type: object
            discriminator:
              propertyName: kind
            xml:
              name: item
            externalDocs:
              url: https://example.com
            x-internal: true
            example:
              kind: a
            """);

        schema.ContainsKey("discriminator").ShouldBeFalse();
        schema.ContainsKey("xml").ShouldBeFalse();
        schema.ContainsKey("externalDocs").ShouldBeFalse();
        schema.ContainsKey("x-internal").ShouldBeFalse();
        schema.ContainsKey("example").ShouldBeFalse();
        schema["examples"]![0]!["kind"]!.GetValue<string>().ShouldBe("a");
    }

    [Fact]
    public async Task Convert_InlineNestedObjects_KeepAllConstraints()
    {
        var schema = await ConvertAsync("""
            type: object
            properties:
              tags:
                type: array
                minItems: 1
                items:
                  type: object
                  required: [name]
                  properties:
                    name:
                      type: string
                      pattern: '^[a-z]+$'
              extra:
                type: object
                additionalProperties:
                  type: integer
            """);

        Validate(schema, """{"tags": [{"name": "abc"}], "extra": {"a": 1}}""").ShouldBeTrue();
        Validate(schema, """{"tags": []}""").ShouldBeFalse();
        Validate(schema, """{"tags": [{"name": "ABC"}]}""").ShouldBeFalse();
        Validate(schema, """{"extra": {"a": "x"}}""").ShouldBeFalse();
    }

    // ═══════════════════════════════════════════════════════════════
    // $ref: циклы, внешние файлы, коллизии имён
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Convert_SelfReferencingSchema_ProducesRecursiveDef()
    {
        const string yaml = """
            openapi: 3.0.3
            components:
              schemas:
                TreeNode:
                  type: object
                  required: [value]
                  properties:
                    value:
                      type: string
                    children:
                      type: array
                      items:
                        $ref: '#/components/schemas/TreeNode'
            """;

        var tree = Parse(yaml);
        var schema = await new JsonSchemaGenerator().BuildSchemaDocumentAsync(
            new Dictionary<string, object?> { ["$ref"] = "#/components/schemas/TreeNode" },
            tree, DocumentKey, DefaultOptions);

        schema["$defs"]!["TreeNode"]!["properties"]!["children"]!["items"]!["$ref"]!
            .GetValue<string>().ShouldBe("#/$defs/TreeNode");
        Validate(schema, """{"value": "a", "children": [{"value": "b", "children": []}]}""").ShouldBeTrue();
        Validate(schema, """{"value": "a", "children": [{"children": []}]}""").ShouldBeFalse();
    }

    [Fact]
    public async Task Convert_ExternalRelativeRef_LoadsFileAndInlinesIntoDefs()
    {
        var loader = new InMemoryLoader(new Dictionary<string, string>
        {
            [Path.GetFullPath("/specs/common/money.yaml")] = """
                Money:
                  type: object
                  required: [amount, currency]
                  properties:
                    amount:
                      type: number
                    currency:
                      $ref: '#/Currency'
                Currency:
                  type: string
                  pattern: '^[A-Z]{3}$'
                """
        });

        const string yaml = """
            openapi: 3.0.3
            paths:
              /payments:
                post:
                  operationId: createPayment
                  requestBody:
                    content:
                      application/json:
                        schema:
                          type: object
                          properties:
                            total:
                              $ref: './common/money.yaml#/Money'
            """;

        var files = await GenerateAsync(yaml, loader: loader, documentKey: Path.GetFullPath(DocumentKey));
        var schema = JsonNode.Parse(files["createPayment.Rq.schema.json"])!.AsObject();

        schema["$defs"]!.AsObject().Select(p => p.Key).ShouldBe(new[] { "Currency", "Money" }, ignoreOrder: true);
        Validate(schema, """{"total": {"amount": 10, "currency": "RUB"}}""").ShouldBeTrue();
        Validate(schema, """{"total": {"amount": 10, "currency": "rub"}}""").ShouldBeFalse();
    }

    [Fact]
    public async Task Convert_SameNameInDifferentFiles_GetsUniqueDefNames()
    {
        var loader = new InMemoryLoader(new Dictionary<string, string>
        {
            [Path.GetFullPath("/specs/other.yaml")] = """
                Address:
                  type: string
                """
        });

        const string yaml = """
            openapi: 3.0.3
            paths:
              /orders:
                post:
                  operationId: createOrder
                  requestBody:
                    content:
                      application/json:
                        schema:
                          type: object
                          properties:
                            billing:
                              $ref: '#/components/schemas/Address'
                            shipping:
                              $ref: './other.yaml#/Address'
            components:
              schemas:
                Address:
                  type: object
            """;

        var files = await GenerateAsync(yaml, loader: loader, documentKey: Path.GetFullPath(DocumentKey));
        var schema = JsonNode.Parse(files["createOrder.Rq.schema.json"])!.AsObject();

        schema["$defs"]!.AsObject().Select(p => p.Key).ShouldBe(new[] { "Address", "Address_2" }, ignoreOrder: true);
        Validate(schema, """{"billing": {}, "shipping": "Moscow"}""").ShouldBeTrue();
        Validate(schema, """{"billing": "Moscow"}""").ShouldBeFalse();
    }

    [Fact]
    public async Task GenerateAll_CyclicRequestBodyRefs_Throws()
    {
        const string yaml = """
            openapi: 3.0.3
            paths:
              /x:
                post:
                  requestBody:
                    $ref: '#/components/requestBodies/A'
            components:
              requestBodies:
                A:
                  $ref: '#/components/requestBodies/B'
                B:
                  $ref: '#/components/requestBodies/A'
            """;

        await Should.ThrowAsync<CyclicReferenceException>(() => GenerateAsync(yaml));
    }

    // ═══════════════════════════════════════════════════════════════
    // End-to-end: валидация реальных тел запросов
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GeneratedSchema_ValidPayload_PassesValidation()
    {
        var schema = await GenerateSchemaAsync(PersonSpec, "createPerson.Rq.schema.json");

        Validate(schema, """{"id": "x", "name": "Иван", "gender": "MALE", "middleName": null}""").ShouldBeTrue();
        // id — readOnly, его можно не передавать
        Validate(schema, """{"name": "Иван"}""").ShouldBeTrue();
    }

    [Theory]
    [InlineData("""{}""")]                                       // нет обязательного name
    [InlineData("""{"name": ""}""")]                            // minLength
    [InlineData("""{"name": "Ivan1"}""")]                       // pattern
    [InlineData("""{"name": "Иван", "gender": "OTHER"}""")]     // enum
    [InlineData("""{"name": "Иван", "age": -1}""")]             // minimum
    [InlineData("""{"name": "Иван", "unknown": 1}""")]          // additionalProperties: false
    public async Task GeneratedSchema_InvalidPayload_FailsValidation(string payload)
    {
        var schema = await GenerateSchemaAsync(PersonSpec, "createPerson.Rq.schema.json");

        Validate(schema, payload).ShouldBeFalse();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Fixtures & helpers
    // ─────────────────────────────────────────────────────────────────────────

    private const string PersonSpec = """
        openapi: 3.0.3
        paths:
          /persons:
            post:
              operationId: createPerson
              requestBody:
                $ref: '#/components/requestBodies/CreatePerson'
            get:
              operationId: listPersons
        components:
          requestBodies:
            CreatePerson:
              required: true
              content:
                application/json:
                  schema:
                    $ref: '#/components/schemas/Person'
          schemas:
            Person:
              type: object
              additionalProperties: false
              required: [id, name]
              properties:
                id:
                  type: string
                  readOnly: true
                name:
                  type: string
                  minLength: 1
                  pattern: '^[А-ЯЁа-яё]+$'
                middleName:
                  type: string
                  nullable: true
                age:
                  type: integer
                  minimum: 0
                gender:
                  $ref: '#/components/schemas/Gender'
            Gender:
              type: string
              enum: [MALE, FEMALE]
        """;

    private const string PersonWithResponsesSpec = """
        openapi: 3.0.3
        paths:
          /persons:
            post:
              operationId: createPerson
              requestBody:
                content:
                  application/json:
                    schema:
                      type: object
              responses:
                '201':
                  description: created
                  content:
                    application/json:
                      schema:
                        type: object
                '400':
                  description: bad request
                  content:
                    application/json:
                      schema:
                        type: object
        """;

    private const string MultiMethodSpec = """
        openapi: 3.0.3
        paths:
          /items:
            post:
              operationId: createItem
              requestBody:
                content:
                  application/json:
                    schema:
                      type: object
          /items/{id}:
            put:
              operationId: replaceItem
              requestBody:
                content:
                  application/json:
                    schema:
                      type: object
            get:
              operationId: getItem
        """;

    private static object? Parse(string yaml) => new YamlDocumentParser().Parse(yaml);

    private static async Task<Dictionary<string, string>> GenerateAsync(
        string yaml,
        JsonSchemaGeneratorOptions? options = null,
        IDocumentLoader? loader = null,
        string documentKey = DocumentKey)
    {
        var sut = new JsonSchemaGenerator(loader ?? new InMemoryLoader([]));
        var files = await sut.GenerateAllAsync(Parse(yaml), documentKey, options ?? DefaultOptions);
        return files.ToDictionary(f => f.FileName, f => f.Content);
    }

    private static async Task<JsonObject> GenerateSchemaAsync(string yaml, string fileName)
    {
        var files = await GenerateAsync(yaml);
        return JsonNode.Parse(files[fileName])!.AsObject();
    }

    private static Task<JsonObject> ConvertAsync(string schemaYaml, JsonSchemaGeneratorOptions? options = null)
    {
        var schema = Parse(schemaYaml);
        return new JsonSchemaGenerator().BuildSchemaDocumentAsync(
            schema, schema, DocumentKey, options ?? DefaultOptions);
    }

    private static bool Validate(JsonObject schema, string json)
    {
        var jsonSchema = Json.Schema.JsonSchema.FromText(schema.ToJsonString());
        var instance = JsonNode.Parse(json);
        return jsonSchema.Evaluate(instance, new EvaluationOptions { OutputFormat = OutputFormat.Flag }).IsValid;
    }

    private sealed class InMemoryLoader(Dictionary<string, string> documents) : IDocumentLoader
    {
        public Task<string> LoadAsync(string normalizedKey, CancellationToken ct)
            => documents.TryGetValue(normalizedKey, out var content)
                ? Task.FromResult(content)
                : throw new FileNotFoundException($"Not found: {normalizedKey}", normalizedKey);
    }
}
