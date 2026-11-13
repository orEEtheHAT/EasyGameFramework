# EasyUI / Setup — STAGING_READY

Подготовлено 2026-11-13 в Library/EasyGameFrameworkWork/Package. Исходные Packages и Assets не изменялись; Unity CLI, Editor, Play Mode и сборки для проверки staging не запускались.

## Состав

- Modules/EasyUI: существующее uGUI ядро window/focus/dialog/radial, русская инструкция, пакетные Tests. Основной контракт владения Input/cursor сохранён.
- Integrations/EasyUI: 7 поставляемых групп Input, Building, Resources, Interaction, Localization, Feedbacks, IntegrationWindows. Полноценные adapters больше не лежат в Samples~. IntegrationWindowBuilder — фабрика authoring-префабов и теперь создаёт 7 definitions с Graphics.
- Settings bridge использует EasySettings.UI.SettingsWindow.Bind, Controller и ISettingsTextProvider. IntegratedSettingsWindow.BindGraphics(controller)/OpenGraphics подключают чужое runtime-состояние без копирования. PreparedChildWindow сохраняет контекст, lifecycle и reentry-защиту для обоих детей.
- Editor/Setup: уникальная Editor assembly EasyGameFramework.Editor.Setup, namespace EasyGameFramework.Editor. Tools > EasyGameFramework > Setup, 12 диагностических модулей, зависимости, guides, public UPM sample discovery/import, welcome один раз на проект/версию. Combat управляется прямым публичным CombatProjectileSetup API; только явное изменение GUI вызывает SetMode, открытие/закрытие не сохраняют режим.
- UnityEngine.UI references исправлены во всех собственных asmdefs, включая sample/tools/tests. Модульных package-version guards на старые IDs нет. Code/asset paths переведены на единый пакет. Исторические отчёты сохранены как исходные свидетельства прежнего этапа.

## Статическая проверка

static-compile.txt: 21 собственная runtime/editor/test/sample/tool assembly компилируется Roslyn с текущими установленными Unity libraries; actual staged Combat Runtime/Editor отдельно собраны как типизированная зависимость Setup. Это НЕ Unity-import, НЕ Player build, НЕ pass-claim тестов нового дерева. Unity source-generators здесь не выполняются.

staging-audit.json: 17 активных собственных assemblies (включая 6 test assemblies), 0 issues в собственных refs/cycle/package-guard/path проверках. Samples~ содержит только Getting Started.

## После CUTOVER

Под атомарной блокировкой: компиляция; EditMode EasyUI.Tests.Setup; PlayMode EasyUI.*; проверка открытия Setup/Modules/Samples и сохранения прежнего Combat choice после закрытия; welcome lifecycle; короткий просмотр Input→Audio/Graphics, Graphics draft/confirmation/rollback, списка/круга; восстановление редактора и освобождение lock. Общие clean install, public UPM import, Player, общая сцена и profiling — у root.

Регрессии перенесены из прежних sample Tests и переносимых Assets fixtures. Вместо зависимости от Workshop.unity добавлена эквивалентная portable radial purchase/resource label проверка. Дополнены графический child lifecycle и Setup read/open/close safety. Старые результаты 53/53 и 23/23 не выдаются за проверку этой новой структуры.

Короткие инструкции: Modules/EasyUI/README.md, Editor/Setup/Guide.ru.md, Integrations/EasyUI/IntegrationWindows/README.md.
