# P3.0. Bootstrap → Gameplay с автоматическим VContainer

[Индекс и статусы](README.md) · [Основной этап P3](p3-world-and-input.md)

## Цель и контракт

Основа приведена к `.agents/skills/unity-bootstrap-vcontainer`: VContainer автоматически создаёт project root и сценовый контейнер. Bootstrap открывает Gameplay в режиме Single; Gameplay также запускается напрямую. Перезапуск заменяет Gameplay и её контейнер, сохраняя App, каталог и навигатор.

Исторически P3.0 проходил через сценовый App, затем глобальный App с Additive и Single, отдельным Bootstrap scope и ручной сборкой Gameplay. 30.10.2026 пользователь поручил перейти к автоматическому жизненному циклу VContainer. Это заменяет прежний контракт «запуск только через Bootstrap». Ранее подтверждённые проверки относятся к прежним схемам; актуальный статус и свидетельства ведутся только в индексе.

## Ответственность

| Владелец | Назначение |
| --- | --- |
| AppLifetimeScope | Глобальный prefab; Auto Run включён, Parent пустой. Регистрирует существующий LevelContentCatalog, persistent singleton SceneNavigator из prefab и BootstrapEntryPoint |
| VContainerSettings | Root Lifetime Scope указывает на App prefab; asset включён в Preloaded Assets. Корень создаёт и сохраняет сам VContainer |
| BootstrapEntryPoint : IStartable | Запускается VContainer в App и вызывает ToGameplay; отдельного Bootstrap scope нет |
| SceneNavigator | Хранит две Eflatun.SceneReference; ToGameplay работает из Bootstrap, RestartGameplay — из Gameplay. Защищает от двойного запроса до sceneLoaded; использует Single |
| GameplayLifetimeScope | Auto Run включён, явный Parent пустой. VContainer автоматически строит контейнер с родителем App. Сохранены LevelAnchor, Camera, Light и Ground; здесь добавляются будущие сценовые регистрации |

Runtime не выключает Auto Run, не вызывает Build/Initialize и не назначает parentReference. UniTask для этой основы не требуется. Глобальный каталог P1 остаётся единственным; LevelView и настоящий Level_01 подключаются в P3.1 после Gate P3.0.

Убраны GameplaySceneLoader с состояниями загрузки, BootstrapLifetimeScope и служебный GameplayEntryPoint. После миграции удалены AppScenes, временный BootstrapSceneSetup и методы ConfigureReferences/ValidateConfiguration из scope-классов. Конфигурация проверяется EditMode-тестами; runtime-код не дублирует проверку каталога P1 и настройку VContainer. Протокол отмены загрузки при ручном уничтожении App больше не является контрактом. Unity и VContainer управляют штатной сменой сцен и освобождением контейнеров. LoadSceneAsync не получает собственной отмены.

## Assets и Inspector

| Asset/сцена | Конфигурация |
| --- | --- |
| Assets/Content/Prefabs/AppLifetimeScope.prefab | Один активный AppLifetimeScope; Auto Run включён; Parent пустой; Content Catalog = существующий Assets/Content/Settings/LevelContentCatalog.asset; Scene Navigator Prefab назначен |
| Assets/Content/Prefabs/SceneNavigator.prefab | Один активный SceneNavigator без LifetimeScope; Bootstrap Scene и Gameplay Scene указывают на соответствующие сохранённые сцены |
| Assets/Content/Settings/VContainerSettings.asset | Root Lifetime Scope = App prefab; ровно одна запись VContainerSettings в Preloaded Assets; другие preload assets сохраняются |
| Bootstrap | Нет scene instances LifetimeScope или SceneNavigator. Камера, Root и пользовательские объекты сохраняются; старый BootstrapLifetimeScope GameObject может остаться без компонента |
| Gameplay | Один активный GameplayLifetimeScope на корневом объекте; Auto Run включён; Parent Object/Type пустые; LevelAnchor/Camera/Light/Ground сохранены; Ground вне LevelAnchor |
| Build Settings | Bootstrap включена первой, Gameplay второй, без повторных записей |

## Сопровождение

Одноразовая миграция сцены и prefab выполнена; проектные команды Configure Scenes и Validate Scenes удалены вместе с временным сетапером. Для текущей основы повторная установка не требуется. Runtime-ссылки сцен находятся в SceneNavigator prefab; пути для загрузки сохранённых сцен в тестах принадлежат только тестовым фикстурам.

Настройка VContainerSettings, prefab и Inspector поддерживается обычными средствами Unity. Корректность сохранённых assets проверяют BootstrapScopeTests, а автоматический запуск и restart — BootstrapPipelineTests. Установщик новой основы остаётся только внутри шаблона скилла.

## Проверки и ручная приёмка

1. В Test Runner → EditMode выполнить `Screw.Tests.Bootstrap.BootstrapScopeTests`: ссылки root/navigator/catalog, Preloaded Assets и порядок сцен сборки, отсутствие scope в Bootstrap, единственный автоматический Gameplay scope и сохранённые сценовые ссылки. Тесты не изменяют Player Settings и не вызывают проектные сетаперы.
2. В Test Runner → PlayMode выполнить `Screw.Tests.Bootstrap.BootstrapPipelineTests`: зарегистрированный IStartable, Single-переход, двойной запрос, автоматический parent, общий каталог/навигатор, restart, уничтожение старого scope и test-only IDisposable, прямой Gameplay без повторной загрузки. Тест заменяет сцены; перед запуском сохранить работу.
3. Открыть только Bootstrap и запустить Play. Ожидаются переход в Gameplay, выгрузка Bootstrap, ровно один App и один SceneNavigator в DontDestroyOnLoad, один GameplayLifetimeScope с Parent = App. Уровень P3.1 пока не создаётся.
4. Остановить Play, открыть только Gameplay и запустить напрямую. Gameplay автоматически получает контейнер и App; ошибок «Gameplay requires Bootstrap» и перенаправления нет.
5. Несколько раз выполнить Stop/Play: нет дублей и новых ошибок. Использовать обычные Enter Play Mode settings; если в проекте отключён Domain Reload, повторить с реальными настройками проекта.
6. Проверить development build: первой открывается Bootstrap и автоматически переходит в Gameplay; нет Editor API и ошибок карт Eflatun. Editor-тесты не подтверждают сборку.

Первый автоматический dispatch и холодное создание root проверяются вручную: test runner может создать root до начала теста. Restart проверяется настоящим SceneNavigator, без тестового загрузчика и временного UI.

**Gate P3.0:** конфигурация сохранена, целевые тесты проходят, пользователь подтвердил Bootstrap, прямой Gameplay, Stop/Play и отсутствие дублей. Наличие кода и успешная валидация Inspector сами по себе Gate не закрывают. Проверка development build учитывается отдельно.

## Документация API

VContainer 1.19.0: [project root](https://vcontainer.hadashikick.jp/scoping/project-root-lifetimescope), [entry points](https://vcontainer.hadashikick.jp/integrations/entrypoint). Проверено через Context7 по официальному репозиторию. Unity 2022.3: [LoadPrefabContents](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/PrefabUtility.LoadPrefabContents.html), [SaveAsPrefabAsset](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/PrefabUtility.SaveAsPrefabAsset.html). Используются существующие Eflatun.SceneReference 5.0.0 и Test Framework 1.1.33; зависимости не обновлялись.
