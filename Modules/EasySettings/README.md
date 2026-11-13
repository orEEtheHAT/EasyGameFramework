# EasySettings — модуль EasyGameFramework

Настройки экрана и графики только для Unity URP. Готовые адаптеры UI, Localization и Movement FOV входят в единый пакет: импортировать их из Samples не нужно.

1. Создайте **Assets → Create → EasyGameFramework → Settings → Defaults**.
2. Добавьте **EasyGameFramework → Settings → Settings Controller** на отдельный объект сцены; назначьте Defaults и камеру.
3. Подключите `SettingsWindow` к EasyUI WindowManager и вызовите `Bind(controller)`. Контроллер оставьте вне отключаемого окна: закрытие меню не завершает графическую сессию.

Общее меню, окна ввода/аудио/языка и их input contexts предоставляет EasyUI. Settings владеет только графикой; общий Apply не изображает атомарную транзакцию чужих настроек. Демонстрация графики: импортировать только **Settings Demo** (имя в общем Package Manager задаёт root), затем **Tools → EasyGameFramework → Settings → Create first-run scene**.

```csharp
var draft = graphics.Draft;
draft.RenderScale = .75f;
graphics.Draft = draft;
var result = graphics.Apply(); // проверить Success и Messages
```

```csharp
graphics.SelectProfile(1);    // скопировать профиль Defaults.Profiles[1] в draft
graphics.Apply();             // Apply явный; профиль сохраняет экран/VSync/FPS
graphics.Cancel();            // сбросить edits или откатить screen preview
graphics.Reset();             // defaults в draft, экран остаётся текущим
```

```csharp
var snapshot = graphics.Capture(); // только подтверждённое состояние
var check = graphics.Validate(snapshot);
if (check.Success) graphics.Restore(snapshot); // экран сохраняется; Preferences у EasySave
```

Для FPS игрока добавьте `FirstPersonFov` возле EasyMovement controller, затем `EasyMovementSettingsFov` возле Settings Controller и назначьте Player Fov. Настраивается base FOV: authored60/70 + base80 даёт80/90. Gameplay zoom/offsets остаются у Movement и не попадают в snapshot. Без такого владельца FOV на выходной Cinemachine Camera недоступен с объяснением.

Display Apply требует подтверждения (`Confirm`) либо откатывается по unscaled timeout (`Revert`). В Editor экранные изменения недоступны. URP/Volume source assets не изменяются; временные overrides освобождаются при disable/destroy. Подробности: [Guide](Documentation~/Guide.md).

Текущий этап — STAGING. Проверки Unity этой структуры выполняются только после CUTOVER; прежние отчёты в Reports являются исторической базой, а не доказательством готовности нового единого пакета.
