# OpenAPI DTO Generator — заметки к презентации

Файл-компаньон к `presentation/index.html` (27 слайдов). Здесь — то, что не влезло на слайды: воспроизводимые команды, точные ответы и список проблем с доказательствами.

---

## 0. Как открыть и показывать

```
start presentation\index.html
```

| Клавиша | Действие |
|---|---|
| `→` `←` `Space` `PageUp/Down` | навигация |
| `Home` / `End` | первый / последний слайд |
| `F` | полный экран |
| `P` | печать → «Сохранить как PDF» (по одной странице на слайд) |

URL с `#17` открывает конкретный слайд.

---

## 1. Цифры

| Что | Значение | Как проверить |
|---|---|---|
| Тестов | **386**, все проходят | `dotnet test OpenApiDtoGenerator.sln` |
| Core.Tests / Generators.Tests / Cli.Tests / Integration.Tests | 154 / 150 / 30 / 52 | по проектам |
| Проектов в решении | 3 библиотеки + 4 тестовых | `OpenApiDtoGenerator.sln` |

Все 386 тестов проверяют текст и поведение генераторов, но **ни один не компилирует сгенерированный код и не выполняет маскировку**.

---

## 2. Карта слайдов

| № | Тема | Статус |
|---|---|---|
| 1 | Титул | |
| 2 | Что получаем из одного файла, статус каждого артефакта | |
| 3 | Путь данных: YAML → дерево → IR → файлы | |
| 4 | Executor, коды выхода, включение генераторов | |
| 5 | Простой пример: вход и фактический выход | |
| 6–7 | Парсинг YAML: `RepresentationModel`, стиль скаляра | |
| 8 | Четыре вида `$ref`, JSON Pointer | |
| 9 | Построение IR, цикл IRBuilder ↔ RefResolver, слабое место | |
| 10 | DTO: TypeMapper и что пишет DtoGenerator | |
| 11 | RuleMapper: правила по свойству | |
| 12 | Каскад (`SetValidator`), `When` и эвристика | |
| 13 | Валидаторы простого примера, расхождение типов и правил | |
| 14 | Замысел маскировки | |
| 15 | Состояние маскировки, проверено запуском | красный |
| 16 | Стратегии маскировки | |
| 17 | Почему JSON Schema идёт по сырому дереву | |
| 18 | Алгоритм обхода и очередь | |
| 19 | `$defs`: ключи, имена, детерминизм | |
| 20 | Конвертация OpenAPI 3.0 → 2020-12 | |
| 21 | Что важно в draft 2020-12 при валидации | |
| 22 | Рантайм и `index.json` | |
| 23 | 386 тестов и что они не проверяют | |
| 24 | Матрица «что собирается» | |
| 25 | Приоритеты исправлений | |
| 26 | Когда подходит и когда нет | |
| 27 | Итог | |

Перекрёстные ссылки в слайдах: слайд 2 → 15, 24; слайд 11 → 24; слайд 5 → 9–10; слайд 3 → 17; слайд 27 → 15, 24, 25.

---

## 3. Простой пример (`samples/open_api_example_simple.yaml`)

Команда (одной строкой, работает в cmd и PowerShell):

```
dotnet run --project src/OpenApiDtoGenerator.Cli -- generate -i samples/open_api_example_simple.yaml -o Generated -n Simple.Dto --json-schema --json-schema-responses
```

Результат (проверено, код 0): 3 DTO, 3 валидатора, `sendPayload.Rq.schema.json`, `sendPayload.Rs.200.schema.json`, `index.json`.

Что видно в выходе:

- `items: { type: array, items: $ref Item }` → `List<object> Items`. Элементы потеряны.
- `priority: integer, enum: [1, 2, 3]` → `int Priority`, enum не проверяется.
- `status` (inline enum) → `string`, валидатор `IsInEnum()` отклоняет любое значение.
- `id`, `sentAt`, `messageId` (uuid, date-time) → `Guid`/`DateTime`, но валидатор вызывает `TryParse(string)`. Три ошибки CS1503 при сборке валидаторов.
- JSON Schema сохраняет `enum` и `$ref` на `Item` через `$defs`.

---

## 4. Парсинг YAML

- `YamlDocumentParser` использует `YamlStream` / `RepresentationModel`, а не типизированную десериализацию. Причина: типизированная модель OpenAPI отбрасывает неизвестные `x-*` и не даёт видеть стиль скаляра.
- Стиль скаляра важен: `'4510'` (quoted) всегда строка, `4510` — int. Правила `minLength`, `pattern` зависят от этого.
- Plain-скаляры: `null`/`~`/пусто → null, `true`/`false` → bool, целое → int/long, дробное → double, иначе строка. `yes` остаётся строкой (это YAML 1.2).
- `1.10` станет double `1.1`. Поэтому версии и коды пишем в кавычках.
- Обрабатывается только `Documents[0]`: multi-document YAML не поддерживается.

---

## 5. `$ref`

- Виды: Local (`#/...`), RelativeFile, AbsoluteFile, Remote (`http(s)://`).
- Порядок в `RefParser.Parse`: `#` → `http(s)://` → абсолютный путь → относительный путь.
- Remote распознаётся, загрузчика нет. Ссылка даёт ошибку.
- JSON Pointer: `~1` → `/`, затем `~0` → `~`.
- Документы кешируются по ключу: один внешний файл парсится один раз.

---

## 6. IR и DTO

- `IRBuilder` строит `SchemaNode` из `components/schemas` и `requestBodies`.
- Цикл: `CycleDetector.Scope` при повторе бросает `CyclicReferenceException`, узел становится `IsLazyRef`.
- **Слабое место:** `BuildSchemaNodeFromInlineAsync` не заходит в `properties` и `items` inline-схем. Следствие: массив-свойство получает `Items = null` → `List<object>`, объект-свойство остаётся `object`. Исправление — рекурсия (приоритет 2).
- `TypeMapper`: `date` → `DateOnly`, `date-time` → `DateTime`, `uuid` → `Guid`, `integer` → `int`, `int64` → `long`, `number` → `double`, `float` → `float`.
- `DtoGenerator`: `[JsonPropertyName]`, XML-doc из `description`, `?` для необязательных не-коллекций, наследование при `allOf` с именованной базой.
- Integer enum не попадает в IR как enum (`ExtractEnum` берёт только строки). Inline enum даёт `string`.

---

## 7. Валидаторы

- `RuleMapper.Map(prop)` → список строк FluentValidation. Чистая функция, тестируется таблицей.
- Формат проверяется через `TryParse(v)`, но тип свойства уже `Guid`/`DateTime`. Это расхождение `TypeMapper` и `RuleMapper`. Исправление — согласовать оба (приоритет 3).
- `When(...)` связывает поля с дискриминатором по подстроке имени (эвристика, хрупкая).
- `SetValidator` работает, если у вложенного класса есть свой валидатор.
- Не генерируются: `uniqueItems`, `maxProperties`, `additionalProperties`.

Воспроизведение ошибок CS1503: команда из раздела 3, затем сборка `Validators/` и `Dto/` в проект `net8.0` с FluentValidation 12.1.0.

---

## 8. Маскировка — состояние

**Замысел:** `[Mask]` атрибуты на DTO, `ToMaskedJson()` в partial-классе, стратегии Full/Partial/FirstChar/YearOnly/Hash. Вызывает разработчик; обычная сериализация не меняется.

**Что проверено:**

1. `dotnet run ... --masking-config mask.yaml` проходит, создаётся 6 файлов маскировки.
2. Сборка `MaskingJsonConverter.cs` падает: CS0120, `GetType()` в статическом методе (строка 55).
3. После временной правки этой строки в копии (вне репозитория) `ToMaskedJson()` возвращает:
   `{"name":"Ivan","priority":2,"items":[]}`. Маскировки нет.

**Причина отсутствия маски:** `MaskingJsonOptions.Default` не регистрирует `MaskingJsonConverter`, у DTO нет `[JsonConverter]`. `System.Text.Json` не читает `[Mask]` сам.

**Почему тесты не поймали:** тесты сравнивают сгенерированный текст, а не результат выполнения.

**Исправление:** исправить `GetType()`, подключить конвертер (на класс или в опции), добавить тест на результат `ToMaskedJson()`.

Также: `FindRuleForField` ищет по имени поля без учёта пути и регистра. Маска применяется только к `string`.

---

## 9. JSON Schema

- Строится по сырому дереву документа: в IR нет `additionalProperties`, `readOnly`, `uniqueItems`, `maxProperties`, `examples`.
- Алгоритм: операции из `paths` по `--json-schema-methods` (по умолчанию `post`) → `requestBody` (`ResolveRefChainAsync`) → медиатипы JSON и `+json` → `ConvertSchemaAsync` (рекурсивно) → очередь `Pending` → файл `$schema` + корень + `$defs`.
- `$defs`: ключ дедупликации — «документ + pointer». Имя — последний сегмент pointer, санитайзер оставляет `a-zA-Z0-9_-.`, при коллизии `_2`. Порядок `SortedDictionary`, diff стабилен.
- Имя резервируется до обхода (`ReserveDefName`), поэтому самоссылка не уходит в бесконечный цикл.
- Корень: если это `$ref`, файл содержит `$ref` на `#/$defs/...` без соседних ключей, как требует OpenAPI 3.0.
- Конвертация: `nullable` → `type: [T, "null"]` или `anyOf` с `null`; `exclusiveMinimum: true` → число; `example` → `examples`; `discriminator`, `xml`, `externalDocs`, `x-*` удаляются; `readOnly` убирается из `required` только для запросов.
- Файлы: `<operationId>.Rq.schema.json`, `<operationId>.Rs.<код>.schema.json` (только с `--json-schema-responses`), `default` → `Rs.default`.
- `index.json` содержит `method`, `path`, `operationId`, `kind`, `statusCode`, `contentType`, `required`, `schemaFile`. Польза открыта: для одного эндпоинта имени файла достаточно.

**Известные ограничения:** `writeOnly` нигде не исключается. `format` — аннотация, пока не включён `RequireFormatValidation`. `discriminator` не переносится: варианты `oneOf` должны различаться полем `enum: [VALUE]`.

---

## 10. Что сломано и порядок исправления

1. **Маскировка**: CS0120 и регистрация конвертера. Тест на результат.
2. **Рекурсия в inline-схемах**: `BuildSchemaNodeFromInlineAsync` заходит в `properties`, `items`, `allOf`.
3. **Типы против правил**: `TypeMapper` и `RuleMapper` должны говорить об одном типе.
4. **enum**: integer enum и `EnumValues` при резолве.
5. **Внешние схемы и HTTP**: схемы из внешних файлов не попадают в граф; HTTP-загрузчика нет.
6. **Имена**: префиксы и суффиксы применяются не везде одинаково.

**Открытые вопросы:**
- Очищать ли каталог вывода от устаревших файлов (сейчас не очищает).
- Нужен ли `index.json`.

---

## 11. Матрица проверок

| Сценарий | Результат |
|---|---|
| `open_api_example_simple.yaml`, DTO | собирается (предупреждения CS8618) |
| `open_api_example_simple.yaml`, валидаторы | не собирается: 3 × CS1503 |
| `openapi_example.yaml`, `--dto-only` | собирается: 13 классов, 21 предупреждение CS8618 |
| `openapi_example.yaml`, всё + маскировка | не собирается: CS1010, CS1503, CS0246, CS0120 |
| `loan_application.yaml`, `--dto-only` | не собирается: CS0246 (нет `Money`, `Address`, `Applicant`, `Collateral`) |
| Маскировка, `ToMaskedJson()` | не работает |
| JSON Schema (оба примера) | работает, валидация через JsonSchema.Net |

---

## 12. Ответы на вопросы

- **Зачем JSON Schema, если есть валидаторы?** Схема проверяет сырое тело до десериализации и читается любым инструментом draft 2020-12. Сейчас это самая зрелая часть.
- **Почему не Kiota или NSwag?** Инструмент генерирует DTO, валидаторы и схемы, а не полный клиент. Для клиента используйте Kiota или NSwag.
- **Почему маскировка отдельным методом?** Логирование вызывает `ToMaskedJson()` явно. Обычная сериализация для клиента не меняется.
- **Можно ли доверять DTO сейчас?** Для простых схем без массивов-объектов, enum и внешних `$ref` — да, если сборка проходит. Для остального проверяйте вручную.
- **Почему 386 тестов, а не 383?** Актуальный запуск показал 154 + 150 + 30 + 52 = 386. Старое число было устаревшим.
