# Подготовка Localization к EasyGameFramework

Фаза: STAGING, 13 ноября 2026. Рабочие исходники — Modules/EasyLocalization и Integrations/EasyLocalization в едином пакете. Старые Packages/Assets не изменялись. Unity текущего проекта пока не проверяет этот staging.

## Состав

- Core: стандартные Unity Localization settings/tables/references, session lifecycle, отменяемые строки/выбор языка, LanguageState, событийные UI/cache.
- Editor: first setup, диагностика исходных ссылок, reviewed preview/apply и восстановление Google sync. Новый mapping по умолчанию — публичный CSV на чтение.
- Production integrations: ResourceNames, BuildingTranslations, InteractionTranslations включены в обычный импорт фреймворка. Module-version guards и зависимость tests от импорта adapters из Samples удалены.
- Samples: First Run и Gameplay Integration содержат только demo UI, демонстрационное действие и builders. Gameplay использует готовые три компонента вместо отдельного общего wrapper.
- Addons для этого модуля не требуются. Google остаётся Editor-возможностью; Native Localization — стандартной runtime-основой. Input владеет localized binding prompt.

## Конкретные изменения

CaptureState больше не возвращает незаметный пустой код до инициализации. Для Save/UI есть IsReady, TryCaptureState и отменяемый CaptureStateAsync. Сохраняется уже выбранный язык, а не ещё загружаемый кандидат. Restore не пишет в SO; Preferences отделены от игровых слотов.

LocalizationDiagnostics читает source collections/entries, диагностирует пустой/повторный ID, отсутствующий table/key, ссылку по имени и пропущенный перевод. RenameKey с ID-ссылкой допустим; пустое значение не считается потерянным переводом. Inspector не исправляет таблицы автоматически.

First setup идемпотентен. Его первоначальная запись ProjectLocale теперь направлена именно в созданный asset, даже если Unity держит другой cached runtime Instance. Ранее static setter мог изменить этот другой экземпляр. Существующие assets/config/ID/строки не перезаписываются. Код root:

```csharp
var mapping = EasyLocalization.Editor.LocalizationSetup.Create(
    "Assets/EasyGameFramework/Framework Playground/Localization",
    "en", new[] { "ru" }, "Framework Playground");
```

В sync проверка cancellation добавлена прямо перед локальным commit и перед вызовом записи транспорта. Ранее отмена из Progress callback непосредственно перед применением могла не остановить синхронное изменение таблицы. Подготовленный журнал остаётся для следующего проверочного preview; отправка не повторяется автоматически.

## Что уже проверено статически

17 собственных сборок компилируются отдельным Roslyn из staged source. Ссылки между ними направлены на свежие собственные DLL; Unity и прочие модули представлены установленными reference assemblies. Это проверяет синтаксис/доступность API, но не заменяет импорт Unity и выполнение tests после CUTOVER.

Статический аудит: references разрешаются; собственных дублей assembly/GUID, старых module-version guards, старых package paths в code/assets и runtime-ссылок на Editor/Google SDK нет. Test assemblies autoReferenced=false; они не требуют sample assemblies. Правильное имя uGUI assembly — UnityEngine.UI.

Доказательства вне Temp: Audits/EasyLocalization/Unification/StaticCompile/results.json, static-audit.json, staged-source-hashes.json. Исторические результаты 0.2.0 перенесены в History и не считаются результатом проверки объединённого пакета.

## Проверки после CUTOVER

По исходникам ожидаются 48 Editor и 31 PlayMode test methods (79 всего):

- Прежние 32 editor/planner/table cases, 7 Google HTTP simulations, 24 lifecycle/text/cache и 3 production-adapter fixture.
- Новые 4 runtime-state cases: холодный capture, committed language/JSON round trip, отмена отдельного capture и изоляция после settings replacement.
- Новые 4 sync cases: preview не меняет данные/baseline, ранняя отмена не читает транспорт, отмена во время чтения не выдаёт план и отмена на границе commit не применяет изменения.
- Новые 5 authoring cases: чистая настройка settings/locales, сохранение чужого cached Instance, повторный setup с сохранением IDs/значений/mapping, стабильная ссылка после rename/явно пустой перевод и диагностика ошибок таблиц/каталога. Первые два аспекта покрыты одним first-setup case.

Все network tests используют fake transport/HTTP handler. Реальные пользовательские Sheets и OAuth не используются. В fixtures восстанавливаются прежние settings/locales; integration teardown дожидается восстановления session.

После переключения root: компиляция, собственные Editor/PlayMode suites через Unity CLI и совместный subset Input/Localization/UI; конкретные failures исправляются в новом корне под lock. Общие Framework Playground, чистая установка, Player и измерения всего комплекта принадлежат координатору. Отдельные полные проекты/сборки этим владельцем в staging не запускаются.
