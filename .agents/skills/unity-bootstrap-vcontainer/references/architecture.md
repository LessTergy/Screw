# Происхождение и контракт

Основа выделена из Arkanoid, Unity `6000.3.18f1`, 30 октября 2026 года. Исходный проект при извлечении не менялся. Это описание наблюдаемой конфигурации и кода, а не отчёт о запуске Editor.

## Как устроен исходный запуск

`PlayerSettings.Preloaded Assets` содержит `Assets/Content/Data/VContainerSettings.asset`. Его `RootLifetimeScope` указывает на `Assets/Content/Prefabs/AppLifetimeScope.prefab`, где включён `autoRun`. В Bootstrap присутствует камера, но нет App scope. VContainer загружает настройки, создаёт единственный корень из prefab и переносит его в DontDestroyOnLoad. Поэтому добавлять App scope в Bootstrap вручную нельзя.

`AppLifetimeScope.Configure` регистрирует диагностический `ScopeLifetimeProbe`, singleton-компонент `SceneNavigator` из prefab с `DontDestroyOnLoad` и `BootstrapEntryPoint : IStartable`. Entry point вызывает `ToGameplay`. Навигатор сравнивает путь активной сцены с Bootstrap; в Gameplay этот вызов ничего не загружает. Это позволяет запускать Gameplay прямо из Editor.

Навигатор хранит две `Eflatun.SceneReference`, блокирует повторный запрос до `sceneLoaded` и использует `LoadSceneMode.Single`. В Gameplay находится один `GameplayLifetimeScope` с `autoRun=true`, без явно выбранного Parent Type. VContainer назначает ему project root. Перезапуск выгружает сцену вместе с её scope и создаёт новый; App scope и navigator сохраняются. `IStartable` корня не запускается заново при возврате в Bootstrap: для последующего перехода нужен явный `ToGameplay`.

Исходный `BootstrapSmokeTests` одновременно проверяет DI, restart, score/combo, кирпичи, input, Addressables и DOTween. Общая часть — загрузка сцен, родительский контейнер, идентичность навигатора, сохранение корня и уничтожение предыдущего scope. Игровые регистрации принадлежат Gameplay scope и в скилл не входят. `Lifetime.Singleton` внутри Gameplay означает singleton этого контейнера и его потомков, а не постоянный сервис всего приложения.

## Что сохранено и что обобщено

| Часть | Перенос |
| --- | --- |
| AppLifetimeScope / BootstrapEntryPoint / SceneNavigator | Та же ответственность, namespace ProjectBootstrap |
| GameplayLifetimeScope | Только жизненный цикл и диагностический probe; точка для будущих игровых регистраций |
| ScopeLifetimeProbe | Создание и Dispose отражаются в Console; не становится глобальным сервисом |
| Eflatun.SceneReference | Сохраняется вместо строк/индексов; сборка без Addressables |
| VContainerSettings / preload / build scenes | Создаются Unity API, без исходных GUID |
| Прямой Gameplay / повторный запрос / restart | Сохранены и покрыты тестами |
| Ошибка синхронного LoadSceneAsync | В шаблоне сбрасывается флаг запроса и ошибка передаётся вызывающему коду |
| DI-тесты | Отдельный test-only IDisposable подтверждает освобождение scene-сервиса |
| Ball, bricks, score, bonuses, input, HUD, pause | Исключены |
| UniTask, DOTween, Addressables, URP, TMP | Не нужны этой основе |
| BootstrapSceneInstaller | Новая Editor-автоматизация, вызывается через MCP; в player assembly не входит |

Шаблон не переключает Play автоматически на Bootstrap через `playModeStartScene`, не вводит RuntimeInitializeOnLoadMethod поверх VContainer и не использует EnqueueParent для обычной Single-загрузки. Две камеры одновременно не живут: Bootstrap выгружается целиком.

Автоматический жизненный цикл — контракт основы, а не один из режимов. `LifetimeScope.Awake` и `autoRun=true` строят контейнер; VContainer выбирает project root как parent и запускает зарегистрированные entry points. Собственные `Initialize/Build`, выключение Auto Run, Bootstrap scope и загрузчик с ручным назначением parent для этой схемы не нужны. Обычная Single-загрузка не отменяется приложением; уничтожение App во время перехода не является отдельным поддерживаемым протоколом. Завершение приложения обслуживают Unity и Dispose контейнеров.

При явно запрошенном приведении существующей основы сохраняются проектные регистрации и сериализованные ссылки. Например, каталог остаётся в App, игровые ссылки и entry point — в Gameplay. Шаблон задаёт запуск и lifetime, а не требует стирать игровую интеграцию. Устаревший scene scope отвязывается до удаления скрипта. Для Unity 2022.3 тесты читают сцены через публичный `EditorSceneManager.OpenScene`; `OpenPreviewScene` в этой версии недоступен.

## Наблюдаемые инварианты

1. Ровно один корневой App scope и один persistent navigator; root prefab не размещается в сценах.
2. Settings присутствует в Preloaded Assets и содержит ссылку именно на prefab-компонент App scope.
3. Bootstrap и Gameplay включены в эффективный build scene list; Bootstrap первый.
4. Gameplay scope строится автоматически, Parent — App scope, его контейнер разрешает тот же navigator.
5. Повторный запрос загрузки во время перехода не создаёт вторую загрузку.
6. Restart сохраняет root/navigator, уничтожает прежний scene scope, освобождает его IDisposable и создаёт новый контейнер.
7. Прямой Gameplay не запускает повторную загрузку из root entry point.
8. Повторная установка не перезаписывает пользовательский код/ассеты и не добавляет дубликаты preload/build entries.

## Источники и версии

- [VContainer project root](https://vcontainer.hadashikick.jp/scoping/project-root-lifetimescope): root prefab и VContainerSettings в preload. Документация получена через Context7 из официального репозитория hadashiA/VContainer.
- [VContainer child scopes](https://vcontainer.hadashikick.jp/scoping/generate-child-via-scene): альтернативные способы parenting; Additive/EnqueueParent не нужны этой схеме.
- API VContainer `1.19.0` дополнительно сверены по установленному исходнику: `VContainerSettings.OnEnable`, `OnFirstSceneLoaded`, `GetOrCreateRootLifetimeScopeInstance`, `LifetimeScope.GetRuntimeParent`.
- [Eflatun.SceneReference](https://github.com/starikcetin/Eflatun.SceneReference): версия `5.0.0`, SceneReference и карты сцен. Context7 не нашёл этот пакет; использованы официальный README и установленный исходник `SceneReference`, `SceneDataMapsGenerator.Run(bool)`. Не заменять документацией другого пакета с похожим именем.
- [Unity PlayerSettings.SetPreloadedAssets](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/PlayerSettings.SetPreloadedAssets.html), [PrefabUtility.SaveAsPrefabAsset](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/PrefabUtility.SaveAsPrefabAsset.html).
- [Unity scene tests](https://docs.unity3d.com/Manual/test-framework/course/scene-based-tests.html): EditorSceneManager и очистка сцен; получено через Context7 из официальной Unity documentation.

При иной версии сначала проверь совместимость. Эти сведения фиксируют происхождение шаблона, а не обещают поддержку всех версий Unity/VContainer.
