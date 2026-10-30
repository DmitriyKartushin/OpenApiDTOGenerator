# OpenAPI DTO Generator — заметки к презентации

Файл-компаньон к `presentation/index.html` (36 слайдов). Здесь — то, что не влезло на слайды:
точные ответы на вопросы, воспроизводимые команды и честный список проблем с доказательствами.

---

## 0. Как открыть и как показывать

```
start presentation\index.html          # просто открыть в браузере
```

| Клавиша | Действие |
|---|---|
| `→` `←` `Space` `PageUp/Down` | навигация |
| `Home` / `End` | первый / последний слайд |
| `F` | полный экран |
| `P` | печать → «Сохранить как PDF» (по одной странице на слайд) |

URL с `#12` открывает конкретный слайд — удобно для «покажите ещё раз слайд про циклы».

---

## 1. Цифры, которые можно называть (все проверены)

| Что | Значение | Как проверить |
|---|---|---|
| Проектов в решении | 3 библиотеки + 4 тестовых | `OpenApiDtoGenerator.sln` |
| Тестов | **383**, все зелёные | `dotnet test` |
| Core.Tests / Generators.Tests / Cli.Tests / Integration.Tests | 154 / 147 / 30 / 52 | по проектам |
| Время полного прогона | ~5 секунд | `dotnet test` |
| Схем в `samples/openapi_example.yaml` | 15 → 13 DTO + 13 валидаторов + 10 файлов маскировки | запуск `generate` |
| Схем в `samples/complex/loan_application.yaml` | 10 → 8 DTO + 9 валидаторов | запуск `--dto-only` |
| JSON Schema на `loan_application.yaml` (`post put`) | 3 файла + `index.json` | `--json-schema-only` |
| Внешние зависимости | YamlDotNet 15.1.2 (Core), System.CommandLine (Cli), FluentValidation (только у сгенерированных валидаторов), JsonSchema.Net (тесты) | `.csproj` |

---

## 2. Три команды, которые стоит держать под рукой

```bash
# 1. Всё сразу на базовом примере
dotnet run --project src/OpenApiDtoGenerator.Cli -- generate -i samples/openapi_example.yaml -o Generated -n MyApp.Dto --masking-config samples/masking-config.yaml --json-schema

# 2. Только JSON Schema, с PUT
dotnet run --project src/OpenApiDtoGenerator.Cli -- generate -i samples/complex/loan_application.yaml -o out -n Loan.Dto --json-schema-only --json-schema-methods post put

# 3. Тесты
dotnet test
```

Коды выхода: `0` успех, `1` непредвиденная ошибка, `2` входной файл не найден,
`3` ошибка формата (YAML/JSON/конфиг маскировки), `4` несовместимые флаги.

---

## 3. Пайплайн в одном абзаце (ответ «расскажите в целом»)

`GenerateCommandExecutor` читает файл → `YamlDocumentParser` (YamlDotNet RepresentationModel)
превращает его в универсальное дерево `Dictionary<string, object?>` / `List<object?>` / скаляры →
`IRBuilder` вместе с `RefResolver` обходит `components/schemas`, резолвит `$ref` (локальные,
относительные, абсолютные, HTTP), ловит циклы и строит `Dictionary<string, SchemaNode>` — это IR →
дальше четыре независимых генератора: `DtoGenerator` (C#-классы), `ValidatorGenerator`
(FluentValidation), `MaskingGenerator` (инфраструктура + partial-классы), `JsonSchemaGenerator`
(draft 2020-12 по **сырому дереву**, а не по IR) → CLI пишет файлы в `Dto/`, `Validators/`,
`Masking/`, `JsonSchema/`.

Ключевая мысль для слушателя: **две разные модели данных**. Три генератора работают по IR,
JSON Schema — по исходному дереву, потому что IR сознательно не хранит `additionalProperties`,
`readOnly`, `examples`, `uniqueItems`, `maxProperties`, `patternProperties`.

---

## 4. Комментарии по слайдам (что говорить)

**1–2. Зачем.** Проблема не в «написать классы», а в рассинхроне: спека изменилась — код нет.
Четыре артефакта из одного источника закрывают: типы, валидацию, приватность логов, проверку
сырого тела запроса.

**3. Пайплайн.** Показать развилку «IR vs сырое дерево» — это первое, о чём спросят.

**4. CLI.** 15 опций (без `--help`), 5 кодов выхода, флаги `--*-only` взаимоисключающие (проверяется в
`Program.cs`, код 4).

**5. Архитектура.** Правило: генераторы возвращают `GeneratedFile(FileName, Content)` и **никогда**
не пишут на диск. Поэтому 147 тестов генераторов сравнивают строки и идут за миллисекунды.

**6. Шесть шагов executor'а.** Отдельно подчеркнуть: `ShouldGenerateMasking => MaskingConfig is not null`
— маскировка включается наличием конфига, а не флагом.

**7–9. Парсинг YAML.** YamlDotNet `RepresentationModel`, а не десериализация в POCO:
1. сохраняется стиль скаляра — `series: '4510'` остаётся `string`, `series: 4510` становится `int`;
2. неизвестные ключи (`x-*`) не ломают разбор;
3. JSON проходит тем же путём (JSON ⊂ YAML);
4. берётся только `Documents[0]` — multi-document YAML (`---`) не поддерживается.

Почему это не педантизм: `minLength`/`pattern` применяются к строке. Если серия паспорта «стала
числом», правило молча исчезнет, а поле в DTO будет `int`.

**10–12. `$ref`.** Четыре вида: `Local`, `RelativeFile`, `AbsoluteFile`, `Remote`.
`RefParser` проверяет в порядке `#` → `http(s)://` → `IsPathRooted` → иначе relative.
`DocumentCache` — `ConcurrentDictionary` + `SemaphoreSlim` на ключ, double-check после захвата.
`JsonPointerResolver` — RFC 6901, экранирование строго `~1`, затем `~0`.
**Честно:** `Remote` классифицируется, но `RemoteHttpLoader` нет — HTTP-`$ref` упадёт.

**13. Циклы.** Две стратегии: IR — `CycleDetector` + `CyclicReferenceException` +
`CycleDetectorScope` (RAII), исключение превращается в узел `IsLazyRef = true`; JSON Schema —
очередь `Pending`: имя резервируется **до** конвертации тела, поэтому самоссылка безопасна.
Побочный эффект в IR: ленивый узел без имени → в DTO `List<object>`.

**14–15. IR.** `SchemaNode` — `record` с `init`, собирается через `with`. Восемь шагов построения.
Слабое место: `BuildSchemaNodeFromInlineAsync` не рекурсирует в `properties`/`items`, поэтому
inline-объекты и inline-массивы деградируют до `object` / `List<object>`.

**16–18. DTO.** `TypeMapper` (таблица `type`+`format` → C#), `NameHelper.ToPascalCase`,
`[JsonPropertyName]` хранит исходное имя, XML-doc из `description`, `?` при `nullable` или
отсутствии в `required`, enum через `[EnumMember]`, `allOf` → наследование.

**19–21. Валидаторы.** `RuleMapper.Map(prop)` возвращает список строк — чистая функция, поэтому
тестируется таблицей. `ValidatorGenerator` добавляет `SetValidator(new XValidator())` для
вложенных объектов и `.When(...)` по дискриминатору (эвристика по именам — честно говорим).

**22–27. JSON Schema.** Зачем, почему мимо IR, алгоритм, конвертация ключевых слов, `$defs`,
формат файла, рантайм-использование. Важные детали:
- `$defs` ключуются по «документ + pointer», поэтому `Money` из двух файлов → `Money` и `Money_2`;
- `readOnly` выкидывается из `required` (иначе 100 % ложная ошибка);
- `exclusiveMinimum: true` + `minimum: N` → `exclusiveMinimum: N` (в 2020-12 это число);
- `format` в 2020-12 — аннотация: нужен `RequireFormatValidation = true`;
- `discriminator` не переносится (не ключевое слово JSON Schema) — варианты `oneOf` должны
  различаться содержимым (в `samples/complex` у них есть поле с `enum`).

**28. Маскировка.** 5 файлов инфраструктуры + `partial` с `ToMaskedJson()`. Шесть стратегий.
Ограничение: только `string`, правило ищется по имени поля без пути.

**29–31. Качество и честность.** 383 теста, но **ни один не компилирует сгенерированный код** —
проверяется текст. Отсюда слайд 30: что происходит при реальной сборке.

**32–35. Честность, кейсы, расширение, FAQ.** См. разделы 5–7 ниже.

**36. Итог.** Три команды на финальном слайде: запустить, посмотреть примеры, проверить.

### Карта слайдов

| № | Тема |
|---|---|
| 1–2 | титул, какую проблему решаем |
| 3–6 | пайплайн, CLI, архитектура, шаги executor'а |
| 7–9 | парсинг YAML (RepresentationModel, скаляры, object-граф) |
| 10–13 | `$ref` (виды, кеш, JSON Pointer), циклы |
| 14–15 | IR: `SchemaNode`, 8 шагов `IRBuilder` |
| 16–18 | DTO: `TypeMapper`, сборка класса, пример |
| 19–21 | FluentValidation: `RuleMapper`, каркас, пример |
| 22–27 | JSON Schema: зачем, алгоритм, ключевые слова, `$defs`, файл, рантайм |
| 28 | маскировка PII |
| 29–32 | тесты, проверка компиляцией, матрица ограничений, приоритеты фиксов |
| 33–35 | кейсы, точки расширения, FAQ |
| 36 | итог |

---

## 5. Кейсы использования (для слайда 32 и ответов)

1. **Contract-first backend.** Спека согласуется до кода; генерация в CI. Расхождение «контракт ↔ код»
   ловится на сборке.
2. **PII в логах (152-ФЗ).** Один объект: обычная сериализация — в ответ, `ToMaskedJson()` — в лог/трейс.
   Правила в YAML, а не в конфиге логгера.
3. **Граница сервиса / gateway / BFF.** JSON Schema проверяет сырое тело до десериализации:
   лишние поля, `uniqueItems`, точный путь ошибки. Файл читают любые языки.
4. **Тестовые данные и моки.** `valid_*.json` / `invalid_*.json` — источник фикстур; схема — оракул.
5. **Контрактные тесты в CI.** Добавили JSON-файл — получили регрессионный тест без C#-кода.
6. **Миграция legacy.** Есть только `swagger.yaml` — получаем типизированный контракт и единые
   сообщения об ошибках.

**Когда не подходит:** нужен полный клиент API (Kiota/NSwag), OpenAPI 3.1 с массивами `type`,
code-first без спеки, готовая DI-регистрация валидаторов.

---

## 6. FAQ — короткие ответы

**Почему не NSwag/Kiota?** Они генерят клиентов и модели. Здесь нет ни клиента, ни HTTP-слоя —
зато есть JSON Schema тела запроса и маскировка PII, которых там нет, и полный контроль над
текстом валидаторов.

**Почему YamlDotNet `RepresentationModel`, а не `Microsoft.OpenApi`?** Нужен универсальный доступ
к дереву: JSON Schema требует **все** ключевые слова, включая vendor-расширения. Объектная модель
OpenAPI их теряет и навязывает второй путь для схем.

**Почему JSON Schema не по IR?** В IR нет `additionalProperties`, `readOnly`, `examples`,
`uniqueItems`, `maxProperties`, `patternProperties`; IR склеивает inline-объекты; для схемы нужны
все `$ref`-цепочки, включая `components/requestBodies` и внешние файлы.

**Генерация детерминирована?** Да: `$defs` — `SortedDictionary`, порядок свойств — порядок в
исходном словаре, имена файлов от `operationId`/метода+пути, коллизии → `_2`. Diff в git стабилен.

**Что при опечатке в YAML?** `FormatException` → код 3, сообщение без стека (стек — с `--verbose`).

**Где валидируются бизнес-правила (контрольная сумма ИНН, возраст)?** Нигде — и это осознанно:
в OpenAPI их не выразить. Пишутся руками поверх сгенерированных валидаторов.

**Можно ли только JSON Schema?** Да, `--json-schema-only`. Сегодня это самый зрелый путь:
52 интеграционных теста валидируют реальные тела через `JsonSchema.Net`.

**Почему `ToMaskedJson()`, а не настройка логгера?** Правила живут рядом с данными и
перегенерируются вместе с DTO; переименовали поле — правило обновили один раз.

**Сколько стоит добавить новый артефакт (например, TypeScript)?** Новый класс в `Generators/`,
возвращает `List<GeneratedFile>`; вызов в executor + флаг в `GenerateOptions`. Core и CLI не меняются.

**Производительность?** Парсинг/резолвинг один раз на запуск, документы кешируются по ключу.
На `loan_application.yaml` полный прогон — доли секунды.

**Безопасность?** Генератор не исполняет спеку: только читает файлы (и потенциально HTTP).
`Masking` hash — SHA-256, первые 8 байт в hex (для сверки без раскрытия).

---

## 7. Что реально не работает (с доказательствами)

Всё ниже воспроизводится: `generate` → положить вывод в пустой `net8.0` проект → `dotnet build`.

| Сценарий | Результат |
|---|---|
| `openapi_example.yaml`, `--dto-only` | ✅ собирается: 0 ошибок, 21 предупреждение CS8618 |
| `openapi_example.yaml` + маскировка (всё) | ❌ 16 ошибок: `CS1010/CS1003/CS1026` — перевод строки внутри `WithMessage("…")` у INN и SNILS |
| то же, сообщения починены | ❌ 12 ошибок: `CS1503` — `DateOnly/DateTime/Guid.TryParse(v)`, где `v` уже `DateOnly/DateTime/Guid`; `CS0246` — валидаторы `ConflictErrorResponse`, `ValidationErrorResponse` без классов; `CS0120` в `MaskingJsonConverter` |
| `loan_application.yaml`, `--dto-only` | ❌ 4 × `CS0246`: нет классов `Money`, `Address` (внешние файлы), `Applicant` (`allOf`), `Collateral` (`oneOf`) |
| `--json-schema-only` (оба примера) | ✅ 52 интеграционных теста, включая `invalid_*` |

Причины и минимальные фиксы:

1. **CS1010 — перевод строки в `WithMessage`.** `description` в YAML — block scalar `>`; значение
   приходит с завершающим `\n`, а экранируется только `"`. Фикс: экранировать `\r`, `\n`, `\t` при
   формировании строкового литерала.
2. **CS1503 — тип уже смаплен.** `RuleMapper` предполагает, что в DTO лежит `string`
   (проверка формата «на строке»), а `TypeMapper` уже отдал `DateOnly`/`DateTime`/`Guid`.
   Фикс: либо не мапить `format` в типы C#, либо генерировать проверки под фактический тип.
3. **CS0246 — валидатор без класса.** `ValidatorGenerator` идёт по IR-схемам, а DTO не
   сгенерировался (inline/`oneOf`/`allOf`/внешний файл). Фикс: генерировать валидатор только при
   наличии DTO.
4. **CS0120 / маскировка.** В статическом методе вызывается `GetType()`; `MaskingJsonOptions.Default`
   не регистрирует конвертер. Фикс: `typeof(MaskingJsonConverter<>)` + `Converters.Add(...)`.
5. **`IsInEnum()` на `string`.** Компилируется, но отклоняет **любое** значение: проверено —
   валидатор вернул `IsValid = False` даже для `"PASSPORT"`. Причина: `Enum.IsDefined(typeof(string), value)`
   всегда `false`. Фикс: `.Must(v => Enum.TryParse<TEnum>(v, out _))`.

Приоритеты (слайд 32): (1) рекурсия в `BuildSchemaNodeFromInlineAsync` — закрывает сразу inline-массив,
inline-объект, `allOf` и циклы; (2) `EnumValues`/`Rules` при резолве `$ref` в `RefResolver`;
(3) единый `ClassNameResolver` для DTO/валидаторов (prefix/suffix сейчас рассинхронизированы);
(4) внешние схемы в тот же IR-граф; (5) `RemoteHttpLoader`; (6) мелочи: `required`/инициализаторы,
`JsonStringEnumConverter`, не генерировать пустые валидаторы, `[Mask]` только на `string`.

---

## 8. Карта кода (куда смотреть при вопросе)

| Вопрос | Файл |
|---|---|
| Как парсится YAML | `src/OpenApiDtoGenerator.Core/Parsing/YamlDocumentParser.cs` |
| Как устроен IR | `Core/Models/SchemaNode.cs`, `PropertyNode.cs`, `ValidationRules.cs` |
| Как строится IR | `Core/Parsing/IRBuilder.cs` |
| `$ref` | `Core/Resolving/RefParser.cs`, `RefResolver.cs`, `DocumentCache.cs` |
| JSON Pointer | `Core/Resolving/JsonPointerResolver.cs` |
| Циклы | `Core/Resolving/CycleDetector.cs`, `CycleDetectorScope.cs` |
| Типы C# | `Generators/Dto/TypeMapper.cs` |
| Классы DTO | `Generators/Dto/DtoGenerator.cs`, `NameHelper.cs` |
| Правила валидации | `Generators/Validators/RuleMapper.cs`, `ValidatorGenerator.cs` |
| JSON Schema | `Generators/JsonSchema/JsonSchemaGenerator.cs`, `JsonSchemaGeneratorOptions.cs` |
| Маскировка | `Generators/Masking/MaskingGenerator.cs`, `MaskingConfig.cs` |
| CLI | `Cli/Program.cs`, `Cli/Commands/GenerateCommandExecutor.cs`, `Cli/Config/GenerateOptions.cs` |
| Сквозные тесты | `tests/Integration.Tests/ComplexRequestJsonSchemaTests.cs` |
| Примеры | `samples/openapi_example.yaml`, `samples/complex/loan_application.yaml`, `samples/masking-config.yaml` |
