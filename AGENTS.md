# AGENTS.md

C# (.NET 8 target, net8.0) tool that turns OpenAPI 3.x YAML/JSON into DTOs, FluentValidation validators, JSON masking, and JSON Schema for request bodies. Solution: `OpenApiDtoGenerator.sln`.

## Commands

- Build: `dotnet build OpenApiDtoGenerator.sln`
- All tests: `dotnet test OpenApiDtoGenerator.sln` (386 tests, ~10s, all passing)
- One project: `dotnet test tests/Core.Tests` (also `Generators.Tests`, `Cli.Tests`, `Integration.Tests`)
- Run the CLI from source (single line, works in cmd): `dotnet run --project src/OpenApiDtoGenerator.Cli -- generate --input samples/openapi_example.yaml --output Generated --namespace MyApp.Dto`. Do not split it with `\` (breaks in cmd and PowerShell).
- No lint, formatter, typecheck, codegen, CI workflows, `global.json`, or `Directory.Build.props`. Build and test are the only verification steps.
- The SDK prints `NETSDK1057` (preview .NET) warnings on every build. They are expected noise.

## Layout

- `src/OpenApiDtoGenerator.Core`: parsing (YAML/JSON), `$ref` resolution (local/relative/absolute; HTTP is classified but has no loader, so it fails), cycle detection, `IRBuilder` producing the `SchemaNode` graph.
- `src/OpenApiDtoGenerator.Generators`: DTO, validator, masking, and JSON Schema generators. Consumes the Core IR. JSON Schema is generated from the raw document tree.
- `src/OpenApiDtoGenerator.Cli`: System.CommandLine entrypoint in `Program.cs`. The assembly name is `openapi-dto-gen`. Command logic lives in `Commands/GenerateCommandExecutor.cs`.
- Dependencies: Cli → Core, Generators; Generators → Core.
- `tests/*` mirror the src projects. `Integration.Tests` covers the full pipeline.

## Tests and fixtures

- Tests load fixtures from `AppContext.BaseDirectory/Fixtures`. The csproj files copy them at build time, so after editing a fixture, run a build before testing.
- `openapi_example.yaml` exists in three places: `samples/`, `tests/Cli.Tests/Fixtures/`, and `tests/Integration.Tests/Fixtures/`. They are currently byte-identical. Update all three together.
- `Integration.Tests.csproj` links `samples/complex/**` into `Fixtures/complex`. `ComplexRequestJsonSchemaTests` runs every `payloads/valid_*.json` (must pass) and `invalid_*.json` (must fail). Add a new case by dropping a file in `samples/complex/payloads/`. Name invalid files after the violation. Rebuild so it gets copied.

## Behavior and gotchas

- Exit codes: 0 success, 1 unexpected error, 2 input file not found, 3 format error (YAML/JSON or masking config), 4 conflicting flags (`--*-only` combined, or `--masking-only` without `--masking-config`).
- Mutually exclusive: `--dto-only`, `--validators-only`, `--masking-only`, `--json-schema-only`.
- JSON Schema file names carry the direction: `<operationId>.Rq.schema.json` for request bodies, `<operationId>.Rs.<code>.schema.json` for responses. Response schemas are generated only with `--json-schema-responses` (together with `--json-schema` or `--json-schema-only`). `index.json` has `kind` (`Rq`/`Rs`) and `statusCode`.
- JSON Schema is draft 2020-12. `format` is annotation-only unless the validator sets `RequireFormatValidation = true` (JsonSchema.Net), which the tests need in order to reject bad formats.
- `discriminator` is not carried into JSON Schema. Discriminated `oneOf` variants must each have an `enum: [VALUE]` discriminator field so they are mutually exclusive.
- Generated output (`Generated/`, `bin/`, `obj/`) is not committed.

## Language and local files

- The README, CLI user messages, and many code comments are in Russian. Identifiers and commit messages are in English. Keep existing language when editing docs or messages.
- `.codeassistant/` is gitignored and holds local agent skills for testing and coverage work. It is not part of the repo.
