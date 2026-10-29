# P1: данные уровней и подготовка проверки

Реализация от 29.10.2026. Статус этапа и закрытие Gate ведутся только в [индексе плана](../development-plan/README.md).

## Что подготовлено

- `Screw.Runtime`: модели экспортной схемы, чтение JSON, структурная валидация, контракт будущих игровых описаний и ScriptableObject с явными ссылками на контент.
- `Screw.Editor`: read-only проверка всех поставленных данных/префабов и команда создания отдельного каталога ссылок.
- `Screw.EditMode.Tests`: проверки реальных Level_01/20 и всех 20 префабов; повреждённого JSON, обязательных полей, ID, владельцев, палитр, цветовых групп и вместимости.
- `Screw.PlayMode.Tests`: проверка runtime-чтения, независимых результатов и обязательного ownerId без использования UnityEditor. Она пригодна для последующего запуска Player-тестов.

Используется уже установленный пакет Unity Newtonsoft **3.2.1** с Json.NET **13**. Версии и состав зависимостей не изменялись. Тестовые сборки явно ссылаются на NUnit/Json.NET; `UNITY_INCLUDE_TESTS` исключает их из обычной игровой сборки. Для типов экспортной схемы добавлен `link.xml`, сохраняющий используемые сериализатором члены при stripping; фактическая Android/IL2CPP-сборка этим не подтверждается.

Исходные `Assets/Content/3D/Levels`, сцены и ProjectSettings не изменялись. Группы конкретных болтов и очереди реальных уровней не создавались: это P3.7/P4C.2. Новые игровые описания не дублируют владельцев и трансформации исходника.

## Контракт для следующих этапов

Код находится в `Assets/Content/Scripts/Runtime/Levels`, namespace `Screw.Levels`.

| API / тип | Назначение |
| --- | --- |
| `LevelDataReader.ReadCatalog(json)` | Читает catalog.json, проверяет schemaVersion=1 и всю структуру каталога |
| `LevelDataReader.ReadLevel(json, schemaVersion)` | Читает отдельный Level_NN.json; версия передаётся из каталога, поскольку отдельный файл её не содержит |
| `LevelSourceCatalogData`, `LevelSourceData`, `PartSourceData`, `BoltSourceData`, `BoltPaletteData` | Сохраняют все поля исходной схемы, включая экспортные пути как метаданные |
| `LevelDataValidator.ValidateLevel/ValidateCatalog` | Отклоняют неверные ID/связи/пути/индексы/количества/числовые значения; данные не исправляют и не мутируют |
| `LevelGameplayData` | Ссылается на исходник по `LevelNumber`; содержит `ColorGroups` и упорядоченные `ContainerGroups` |
| `ColorGroupData` | `Id` — позиция в allowedBoltPaletteIndices, `BoltIds` — все болты одного цвета по всему уровню |
| `LevelDataValidator.ValidateGameplay(source, gameplay)` | Проверяет независимость от владельцев, покрытие boltId, кратность по цветам и точную вместимость контейнеров |
| `LevelContentCatalog` | Хранит ссылку на исходный catalog.json, 20 пар JSON/prefab и 12 наборов prefab/двух материалов |
| `LevelContentCatalog.GetLevel(number)/GetColor(index)` | Возвращают явные ссылки на текущие assets; отсутствующий ключ вызывает явную ошибку |

Для группы g её цвет — `source.AllowedBoltPaletteIndices[g]`, **не сам g**. Например, при P=[7,6,8,5,3] группа 0 означает белый индекс 7, группа 1 — розовый индекс 6. Повтор цвета в будущей раскладке пополняет ту же группу.

`ContainerGroups` содержит groupId каждого контейнера: первые два — начальные левый/правый, остальные — общая очередь. Конкретный порядок/подготовка по W реализуются позднее. Валидатор P1 проверяет согласованность вместимости и ссылок, но не создаёт W и не доказывает геометрическую решаемость.

Вместимости фиксированы: `ContainerCapacity=3`, `PoolCapacity=5`. Проверка не требует три болта на деталь. Тестовые фикстуры 2/5/10 цветов включают детали с 1 и 11 креплениями, разные цвета одной детали и один цвет на разных деталях. Они не становятся производственными раскладками.

Повреждённые данные приводят к `InvalidDataException` с причиной; исключение JSON сохраняется как InnerException. Обязательные поля не подменяются значениями по умолчанию, повторные JSON-ключи отклоняются. Числовые значения экспортной схемы хранятся как double для сохранения исходного JSON; переход к Unity Vector3/Quaternion выполняется в проверке привязок. Чтение не загружает старые пути Assets/UnscrewFirst20 и не использует AssetDatabase в runtime.

## Действия пользователя в Unity

1. Открыть **Tools → Screw → Levels → Create Content Catalog**. Команда сначала проверит поставку, затем создаст **новый** `Assets/Content/Settings/LevelContentCatalog.asset` с явными ссылками. Уровни, JSON, меши и материалы она не сохраняет/не меняет. Существующий путь каталога не перезаписывается, в том числе если файл повреждён. Команда не запускалась агентом: постоянное назначение ссылок оставлено пользователю по правилам проекта.
2. Выбрать созданный asset. Проверить: Source Catalog — текущий catalog.json; Levels — 20 элементов, номера 1…20; Palette — 12 элементов 0…11. У Level_01 должна быть ссылка на `Assets/Content/3D/Levels/Prefabs/Level_01.prefab`, у палитры 1 — материалы `2_yellow_1` и `2_yellow_2`. Назначение этого каталога будущему загрузчику/сцене относится к последующим этапам; сейчас их нет.
3. При необходимости повторить read-only проверку через **Tools → Screw → Levels → Validate Imported Content**. Ожидаемый итог Console: `Validated 20 source levels, 692 parts, 1611 bolts and 12 palette references.` Отсутствующий/неверный asset должен давать явную ошибку, а не пропуск уровня.
4. Открыть **Window → General → Test Runner**. В **EditMode** запустить сборку `Screw.EditMode.Tests`, в **PlayMode** — `Screw.PlayMode.Tests`. Подтвердить обнаружение и успешное выполнение; передать результаты/ошибки. Негативные случаи ожидают исключения через NUnit и должны завершаться Passed, а не оставлять неотловленные ошибки.
5. Для P1.4 проверить настроенные Android SDK/NDK/JDK, подключение и доступность устройства для будущей приёмки. Наличие Android Build Support уже обнаружено агентом; наличие и подключение устройства не проверялись. Сборка/запуск игры на Android в этой задаче не выполнялись.

Если каталог ссылок редактируется вручную, его можно проверить вызовом `LevelContentCatalogUtility.ValidateReferences(catalog)` из редакторского кода. При повреждённой конфигурации не пересоздавать asset поверх существующего; исправить конкретную ссылку и повторить проверку.

## Свидетельства текущей проверки

29.10.2026 разрешённым Unity MCP выполнены:

- Обновление/компиляция скриптов; загружены `Screw.Runtime`, `Screw.Editor`, `Screw.EditMode.Tests`, `Screw.PlayMode.Tests`.
- Чтение Console после исправления ссылок тестовых сборок: ошибок/предупреждений нет.
- Прямой read-only вызов `LevelContentCatalogUtility.ValidateImportedAssets()`: все 20 моделей, 692 детали, 1611 болтов и 12 наборов палитры согласованы.
- Независимая проверка JSON → DTO → JSON для всех 20 отдельных записей: полное равенство значений подтверждено через JToken.DeepEquals.
- `BuildPipeline.IsBuildTargetSupported(Android, Android)` вернул true для установленного Editor.

**На момент передачи пользователю агент не выполнял:** Test Runner, Play Mode, создание постоянного каталога, назначение Inspector/сцен, Android-сборку и проверку устройства. Результаты компиляции и прямой валидации сами по себе не заменяют пользовательскую приёмку. После любых последующих правок необходимо повторить затронутые проверки.

После выполнения пользовательской части 29.10.2026 получено подтверждение: «Всё проверил. Закрываем этап». Оно зафиксировано в [едином источнике статусов и Gate](../development-plan/README.md). Подробные тестовые логи, число пройденных тестов и параметры устройства отдельно не предоставлялись; подтверждение P1 не означает выполнение будущей Android-приёмки готовой игры из P6.

Документация, использованная при реализации: [Unity Test Framework 1.1](https://docs.unity3d.com/Packages/com.unity.test-framework@1.1/manual/workflow-create-test-assembly.html), [формат asmdef](https://docs.unity3d.com/2022.3/Documentation/Manual/ScriptCompilationAssemblyDefinitionFiles.html), [SerializedObject](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/SerializedObject.html), [Json.NET: required properties](https://www.newtonsoft.com/json/help/html/JsonPropertyRequired.htm), [JsonLoadSettings](https://www.newtonsoft.com/json/help/html/T_Newtonsoft_Json_Linq_JsonLoadSettings.htm).
