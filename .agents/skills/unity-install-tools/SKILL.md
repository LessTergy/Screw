---
name: unity-install-tools
description: "Инициализация инструментов нового Unity-проекта: VContainer, UniTask, Eflatun.SceneReference, CoplayDev Unity MCP, официальный TextMeshPro и DOTween в Assets/3rd-Party. Используй для установки этого стартового набора; обновление зависимостей существующего проекта выполняй только по отдельному запросу."
---

# Стартовый набор инструментов Unity

Установи перечисленные ниже зависимости в выбранный пользователем Unity-проект. Общайся на русском. Skill содержит инструкции установки; при его создании зависимости в исходном проекте не изменялись.

## Контекст и границы

- Набор взят из `Packages/manifest.json` и `Packages/packages-lock.json` проекта Arkanoid 28 октября 2026 года, Unity `6000.3.18f1`. В обоих файлах обнаружены ровно четыре прямые GitHub-зависимости; все включены ниже. Другие пакеты исходного проекта не являются частью набора, кроме явно запрошенного официального TextMeshPro.
- До изменений прочитай инструкции целевого проекта, `ProjectSettings/ProjectVersion.txt`, manifest и имеющийся lock-файл. Если корень проекта неоднозначен, уточни его. Для UPM Git-зависимостей нужен Git в PATH.
- Установка включает изменения manifest и добавление отсутствующего DOTween в `Assets/3rd-Party`. Не меняй сцены, префабы, настройки проекта или Inspector. Не запускай Unity, Play Mode, тесты или Unity MCP без прямого запроса. Наличие пакета Unity MCP не разрешает управление Editor и не включает настройку MCP-клиента, сервера, Python или uv.
- Сохраняй пользовательские изменения. Повторное применение должно оставлять уже установленный набор без изменений. При другой существующей версии, форке, embedded-пакете, копии DLL или конфликте источников сообщи о конфликте и не перезаписывай затронутую зависимость без отдельного запроса на обновление/миграцию. Независимые отсутствующие зависимости можно установить.
- Базовый набор рассчитан на Unity 6.3. Для другой версии сначала проверь совместимость по официальным источникам через Context7; если точной документации там нет, обратись к официальной документации. Не подбирай версии пробами и не обновляй набор до latest автоматически.

## 1. GitHub-пакеты через UPM

Добавь отсутствующие записи в существующий объект `dependencies` файла `Packages/manifest.json`. Это фрагмент для слияния, а не замена целого manifest:

```json
{
  "com.coplaydev.unity-mcp": "https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#30d22075093d1d35dfb0091c1c7550e9ad948577",
  "com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask#ceac8d6946b1125fe782cd171fbcb245b567dbf9",
  "com.eflatun.scenereference": "git+https://github.com/starikcetin/Eflatun.SceneReference.git#5.0.0",
  "jp.hadashikick.vcontainer": "https://github.com/hadashiA/VContainer.git?path=VContainer/Assets/VContainer#1.19.0"
}
```

| Пакет | Фиксация и происхождение |
| --- | --- |
| CoplayDev Unity MCP | В исходном manifest использовался `#main`; здесь закреплён полный commit из lock-файла. |
| UniTask | В исходном manifest URL был без revision; здесь закреплён полный commit из lock-файла. |
| Eflatun.SceneReference | Сохранён тег `5.0.0`; исходный resolved commit `6821c571090fc0828accced5dbd7635152c23887`. |
| VContainer | Сохранён тег `1.19.0`; исходный resolved commit `5401e5a7ebc4980a2b82141ffc26391a6547edd7`. |

Сохрани остальные зависимости, `scopedRegistries` и другие поля, по возможности форматирование и порядок записей. До записи проверь корректность JSON и отсутствие повторяющихся ключей. Не копируй lock-файл Arkanoid и не редактируй/удаляй lock-файл целевого проекта вручную: Unity обновит его после разрешения зависимостей. При недоступности закреплённого revision сообщи об ошибке, не переключайся на ветку или latest.

## 2. Обязательный официальный TextMeshPro

Используй только пакет Unity из официального Unity Registry/поставки Editor, без GitHub-форков или сторонних архивов.

- В Unity 6 TextMeshPro объединён с Unity UI (uGUI) начиная с uGUI `2.0.0`. Для базовой Unity `6000.3` убедись, что в `dependencies` есть `"com.unity.ugui": "2.0.0"`; добавь запись, если она отсутствует. Уже имеющуюся совместимую официальную версию сохрани. Это установка официального TMP, даже если отдельного `com.unity.textmeshpro` в manifest нет.
- Не добавляй рядом старый standalone `com.unity.textmeshpro`. Если он уже есть, оцени совместимость по документации соответствующего Editor и сообщи о необходимой миграции, не удаляя его автоматически.
- Для старого Editor, где TMP ещё отдельный пакет, используй официальный `com.unity.textmeshpro` с подтверждённой для этого Editor версией. Не подставляй версию из Unity 6.
- Ресурсы зависят от версии пакета. Для версий с отдельным импортом передай пользователю действие `Window > TextMeshPro > Import TMP Essential Resources`, если ресурсы ещё не импортированы. В новых версиях, где настройки, шрифты и шейдеры уже поставляются с пакетом, отдельный импорт не нужен: используй инструкцию установленной версии. Examples & Extras не устанавливай по умолчанию. Не перемещай TMP Resources в `3rd-Party` и не изменяй существующие настройки/шрифты.

## 3. DOTween: скачать, распаковать, перенести

Бесплатный официальный DOTween `1.3.045`:

- Прямая ссылка: https://dotween.demigiant.com/downloads/DOTween_1_3_045.zip
- Страница автора: https://dotween.demigiant.com/download.php
- SHA-256 проверенного архива: `32eba5c48520a541775d00cb396e18975b83587ba79175dbb878d753725ba5d9`.
- Минимальная версия Unity для этого архива: `2022.3`. Автор отдельно указывает для Unity `6000.6` проблему обновления meta и исправление в версии из Asset Store; для такого Editor сначала проверь актуальную инструкцию автора и используй указанный им способ, не обещая корректность ZIP-установки.

Порядок установки:

1. Проверь наличие DOTween в `Assets` по именам файлов/каталогов и пакетным метаданным, включая `DOTween.dll`, каталоги `Demigiant/DOTween` и Pro. Не читай сцены/префабы. Если установка уже есть, пропусти скачивание и перенос, укажи её расположение. Не создавай вторую копию и не перезаписывай DLL или `.meta`. Обновление и перенос существующей установки — отдельная задача.
2. Скачай ZIP по указанному HTTPS URL во вновь созданный временный каталог вне `Assets` (на Windows, например, через `Invoke-WebRequest -Uri ... -OutFile ... -TimeoutSec 30`). Если сеть ограничена, используй штатное разрешение среды на эту операцию; при отказе сообщи о блокировке, не заменяй источник зеркалом.
3. Проверь, что скачан читаемый ZIP, а не HTML ошибки, и сверь SHA-256 (`Get-FileHash -Algorithm SHA256`). При несовпадении останови установку DOTween и сообщи об этом; не принимай новую сумму автоматически.
4. Перед распаковкой проверь имена ZIP-записей: абсолютные пути и выход через `..` недопустимы. Распакуй в отдельный пустой временный подкаталог (`Expand-Archive -LiteralPath ... -DestinationPath ...` на Windows). Проверенный архив содержит корневую папку `DOTween/`, внутри которой находятся `DOTween.dll`, `Editor/`, `Modules/` и документация. Проверь эту структуру; при отличии не угадывай пути.
5. Создай отсутствующий каталог `<project>/Assets/3rd-Party/Demigiant`. Убедись, что полный путь назначения находится внутри выбранного проекта, а `DOTween` и `DOTween.meta` в назначении отсутствуют. Перенеси распакованную папку целиком в `<project>/Assets/3rd-Party/Demigiant/DOTween`, например с `Move-Item -LiteralPath <staging>/DOTween -Destination <project>/Assets/3rd-Party/Demigiant/DOTween`. На Windows используй один PowerShell-процесс для проверки путей и переноса. Сохрани все поставляемые `.meta`, включая соседний `DOTween.meta`, если он есть; не генерируй GUID вручную. Результат: `Assets/3rd-Party/Demigiant/DOTween/DOTween.dll`, без лишнего уровня `DOTween/DOTween`.
6. Оставь DLL, Editor, Modules и документы вместе; не помещай корень DOTween внутрь каталога `Editor` или `Resources`. ZIP и временные файлы не должны попадать в Assets или Git. При очистке удаляй только свой проверенный временный каталог.
7. Передай пользователю обязательную настройку после импорта Unity: `Tools > Demigiant > DOTween Utility Panel > Setup DOTween...` (название пункта может отличаться по версии). Проверить UI/TMP-модули и при необходимости выполнить `Generate ASMDEF` в этой панели. Не редактируй DLL, исходники DOTween или ProjectSettings для имитации Setup. `UNITASK_DOTWEEN_SUPPORT` и дополнительные интеграционные defines не включай без отдельной задачи.

## 4. Проверка и передача результата

- Повторно прочитай manifest как JSON, проверь наличие всех четырёх GitHub-зависимостей и официального поставщика TMP. Сверь diff: изменения ограничены установкой запрошенного набора.
- Для новой ZIP-установки проверь контрольную сумму скачанного архива и наличие ожидаемых файлов в целевом каталоге. Повторный запуск не должен создавать вложенный/дублирующий DOTween или менять уже установленные версии.
- Сообщи, что добавлено, что уже было установлено и какие конфликты остались. Различай изменение manifest, скачивание файлов и фактическое разрешение/компиляцию пакетов Unity.
- Пользователь открывает проект, дожидается завершения Package Manager и компиляции; проверяет все четыре GitHub-пакета и Unity UI/TMP в Package Manager. Затем выполняет нужный для его версии импорт TMP Resources и DOTween Setup. Ожидается отсутствие ошибок компиляции, доступный TMP Text и корректно настроенные модули DOTween. Не создавай тестовую сцену автоматически.
- Не объявляй Editor-настройку, компиляцию или поведение проверенными без результата от пользователя либо отдельно разрешённой проверки. Сохрани сгенерированный Unity lock-файл вместе с изменениями проекта после успешного разрешения пакетов.

## Источники

- Unity UPM Git URLs и revisions: https://docs.unity3d.com/Manual/upm-git.html
- Unity lock-файл: https://docs.unity3d.com/Manual/upm-conflicts-auto.html
- VContainer: https://vcontainer.hadashikick.jp/getting-started/installation
- UniTask: https://github.com/Cysharp/UniTask#upm-package
- Eflatun.SceneReference: https://github.com/starikcetin/Eflatun.SceneReference#installation
- CoplayDev Unity MCP: https://github.com/CoplayDev/unity-mcp
- Официальное объединение TMP с uGUI 2.0: https://docs.unity3d.com/Packages/com.unity.ugui@2.0/changelog/CHANGELOG.html
- TMP, документация поставщика (версию следует сопоставить с установленным пакетом): https://github.com/Unity-Technologies/uGUI/blob/main/com.unity.ugui/Documentation~/TextMeshPro/TextMeshPro.md
- TMP с отдельным импортом Essential Resources: https://docs.unity3d.com/Packages/com.unity.textmeshpro@4.0/manual/index.html
- DOTween: https://dotween.demigiant.com/getstarted.php и https://dotween.demigiant.com/download.php
