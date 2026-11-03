# P3.1: привязки игрового экземпляра Level_01

Это инструкция для P3.1 после [подэтапа P3.0](../development-plan/p3-0-bootstrap-di.md). Единый источник статусов и свидетельств приёмки — [индекс плана](../development-plan/README.md). По прямому поручению пользователя через Unity MCP собран и сохранён Gameplay с настоящим Level_01, назначены привязки и ссылка в GameplayLifetimeScope. Исходные уровни и каталог P1 не изменялись. Шаги ниже описывают воспроизведение настройки; повторно создавать уже сохранённый экземпляр не нужно.

## Что добавлено

- `Screw.Levels.LevelView` — компонент на отдельном объекте сцены, содержащий ссылку на каталог P1, номер уровня, корень экземпляра и сериализованные привязки деталей/болтов. На исходный prefab этот компонент не добавляется.
- `LevelPartBinding` связывает partId с Transform детали. `LevelBoltBinding` связывает boltId с Transform болта и исходным ownerId. Болты остаются в ветке Bolts, а не переподчиняются деталям.
- `LevelView.ReadSource()` использует существующие `LevelContentCatalog.GetLevel`, `LevelDataReader` и валидаторы P1. `ValidateBindings()` дополнительно проверяет узлы конкретного экземпляра: количество/ID, пути, владельцев, принадлежность иерархии, исходные масштабы и совмещение Model/Bolts.
- `GetPart(id)` и `GetBolt(id)` возвращают привязки по ID, независимо от порядка элементов Inspector. Неизвестный ID приводит к явной ошибке. Коллекции снаружи доступны только для чтения.
- `GameplayLifetimeScope.Configure` регистрирует назначенный `LevelView` через `RegisterComponent`. VContainer вызывает `LevelView.Construct` с каталогом корневого App: другой каталог отклоняется, затем проверяются сохранённые привязки. Ссылка Inspector не заменяется и каталог не регистрируется повторно в Gameplay.
- `LevelViewBindingUtility` добавляет команды Bind/Validate Selected Level View. Bind заполняет только поля выбранного компонента, использует существующую проверку префабов P1 и поддерживает Undo через SerializedObject. Команды запрещены в Play Mode, для prefab assets и в Prefab Mode. Validate проверяет текущее состояние без исправлений.

Runtime-компонент не ищет зависимости по сцене и не использует UnityEditor. Редакторская команда один раз разрешает экспортные nodePath внутри явно выбранного экземпляра и сохраняет ссылки. В Play Mode DI проверяет готовые ссылки, ничего не создавая и не заполняя. Коллайдеры игрового экземпляра описаны в [инструкции P3.2](level-colliders.md). Орбитальное управление камерой, выбор, W и цветовая раскладка ещё не подключены.

## Сборка стенда пользователем

1. После принятого P3.0 дождаться компиляции и открыть `Assets/Content/Scenes/Gameplay.unity` вне Play Mode и Prefab Mode. Под LevelAnchor создать пустой объект `LevelPreview`, оставить ему локальные Position/Rotation = 0 и Scale = 1. Добавить компонент **Level View**. Ссылку на этот компонент явно назначить в поле **Level View** у GameplayLifetimeScope. Регистрация и проверка каталога из корневого DI уже реализованы.
2. Перетащить `Assets/Content/3D/Levels/Prefabs/Level_01.prefab` из Project прямо под `LevelPreview`. Сохранить связь экземпляра с prefab. У дочернего Level_01 должны остаться локальные Position/Rotation = 0 и Scale = 1. Внутренние Assembly/Model/Bolts и детали не перемещать, масштабы из JSON повторно не применять.
3. На Level View назначить **Content Catalog** = существующий `Assets/Content/Settings/LevelContentCatalog.asset` из P1, **Level Number** = 1, **Level Root** = дочерний экземпляр Level_01. Не пересоздавать каталог и не выбирать prefab asset из окна Project в поле Level Root. Если каталог P1 отсутствует, сначала восстановить пользовательскую настройку по [инструкции P1](foundation.md).
4. Выбрать `LevelPreview` и выполнить **Tools → Screw → Levels → Bind Selected Level View**. Ожидается сообщение `Bound level 1: 4 parts and 9 bolts.` Поля Assembly Root, Model Root, Bolts Root, Parts и Bolts заполнятся. Повторный запуск даёт те же ссылки и не меняет геометрию.
5. Выполнить **Tools → Screw → Levels → Validate Selected Level View**. Ожидается `Validated level 1 instance bindings.` У каждого болта Owner Id должен совпадать с исходным JSON; совпадение цвета здесь не используется.
6. Использовать существующий Ground из Gameplay (P3.0), расположенный вне LevelAnchor. Сохранённые Scale = (2,1,2), Position ≈ (0.0002, −7.0311, 0.1604): горизонтально на 0.1 единицы ниже минимального Y совокупных Renderer.bounds настоящего Level_01. Это отдельная площадка будущего P4, не игровая деталь и не новая запись в Parts. При перемещении всей фигуры заново проверить зазор площадки.
7. Выбрать Level_01 и нажать F в Scene View. Проверить, что детали и белые болты совмещены, как в исходном prefab. Для размещения всей фигуры перемещать внешний `LevelPreview`; внутренние относительные трансформации сохранять. Базовая камера Gameplay создаётся в P3.0; её кадрирование по модели и орбитальное управление добавляются в P3.3.
8. Сохранить Gameplay и выполнить штатный запуск из Bootstrap. Убедиться, что в Gameplay используется зарегистрированный LevelView с тем же каталогом, который предоставил AppLifetimeScope. Не применять изменения к исходному prefab через Apply. Отдельная сцена LevelPreview не является производственным входом проекта.

Команда Bind не создаёт сцену, площадку или экземпляр уровня: перед её запуском должны быть выполнены шаги 1–3. При повреждённых связях Validate сообщает причину и не меняет поля; исправить конкретную настройку и повторить проверку.

## Проверка P3.1

В **Window → General → Test Runner → EditMode** запустить `Screw.Tests.Levels.LevelViewTests` в обычной сцене вне Prefab Mode. Ожидается Passed для всех случаев:

- Реальный Level_01 содержит ровно 4 привязки деталей и 9 привязок болтов с исходными владельцами; заполнение повторяемо, трансформации/каталог/исходные assets не изменяются.
- Порядок массивов и размещение внешнего объекта не меняют идентичность привязок.
- Пропуски, повторные/неизвестные ID, неверные узлы/владельцы и отсутствующие обязательные ссылки отклоняются.
- Смещение Model или Bolts, изменение масштаба Assembly/Level_01 или позы болта выявляются.
- Узел другого экземпляра с тем же именем и prefab другого уровня не принимаются.
- Инъекция принимает тот же каталог, отклоняет другой без замены сохранённой ссылки и отклоняет неподготовленные привязки.

Тесты создают временные экземпляры и временный каталог в памяти существующей утилитой P1. Постоянный каталог не создаётся/не перезаписывается, сцена и исходные assets не сохраняются. Runtime-валидация принимает сохранённые ссылки; она не восстанавливает повреждённые данные.

Дополнительно запустить `Screw.Tests.Bootstrap.BootstrapScopeTests` (EditMode) и `Screw.Tests.Bootstrap.BootstrapPipelineTests` (PlayMode). Первый проверяет сохранённые ссылки LevelView, исходный prefab, каталог и 4/9 привязок. Второй проверяет зарегистрированный LevelView и общий каталог при Bootstrap → Gameplay, restart и прямой загрузке Gameplay. Перед PlayMode-тестом сохранить сцены: тест заменяет загруженную сцену.

По поручению пользователя агент выполнил через Unity MCP сборку Gameplay, компиляцию, целевые EditMode/PlayMode-тесты, отдельный штатный запуск из Bootstrap и визуальный осмотр с двух сторон. Результаты и оставшаяся пользовательская приёмка записаны только в индексе. Перед P3.2 требуется подтверждение пользователем результата P3.1. Основание — [task-execution, шаг 4](../../.agents/skills/task-execution/SKILL.md): «Не начинай следующий, пока результат и проверка предыдущего не подтверждены».

API сверены через Context7 и официальную документацию Unity 2022.3: [SerializedObject и Undo](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/SerializedObject.html), [PrefabStageUtility.GetPrefabStage](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/SceneManagement.PrefabStageUtility.GetPrefabStage.html), [PrefabUtility.InstantiatePrefab](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/PrefabUtility.InstantiatePrefab.html). Context7 не вернул точные сведения о PrefabStage для 2022.3; эта часть уточнена по официальному первоисточнику.

Регистрация и метод инъекции сверены через Context7 по [исходной документации VContainer](https://github.com/hadashia/vcontainer/blob/master/website/docs/registering/register-monobehaviour.mdx). Для сборки сцены Context7 не вернул точный ответ об InstantiatePrefab; сигнатуры дополнительно подтверждены reflection в установленном Unity и официальной документацией 2022.3.
