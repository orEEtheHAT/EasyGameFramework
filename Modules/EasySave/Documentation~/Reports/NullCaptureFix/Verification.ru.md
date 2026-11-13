# Исправление null JSON capture — 13 ноября 2026

Исправлен P1 из `Audits/EasyGameFramework/20261113-Review/Review.ru.txt`: `JsonUtility.ToJson(null)` мог выдавать пустой payload, который Save успешно записывал, но собственный JSON-адаптер не мог загрузить. Повторная запись могла вытеснить корректный backup.

## Изменение

Runtime diff ограничен `Runtime/JsonSaveParticipant.cs`: 15 добавленных и 3 удалённых строки. Capture вызывает provider один раз, отвергает null DTO до сериализации, затем проверяет JSON object envelope. Parse использует тот же частный guard с прежним условием. Общее `SaveService.CheckPayload`, контейнер, storage и публичные сигнатуры не менялись. Игра по-прежнему владеет Validate; он автоматически не вызывается во время Capture. Serializable structs/default values, marker DTO `{}` и пустые raw payload пользовательского ISaveParticipant сохраняют прежнюю семантику.

Ошибка Capture возвращается существующим сервисом как `CaptureFailed`, `Committed=false`, `FailedParticipant=<key>`, `WorldMayHaveChanged=false`. До storage.WriteAsync выполнение не доходит. Fix не исправляет ранее записанные плохие слоты и не добавляет автоматическую загрузку backup при ValidationFailed.

## Регрессии и результаты

Unity 6000.3.22f1, существующий Games Factory:

| Проверка | Результат |
| --- | --- |
| Импорт/компиляция | errors=[], compilationFailed=false; новая test assembly загружена |
| EditMode, assembly filter `EasySave` | **50/50 passed**, failed/skipped/inconclusive=0 |
| В том числе новые JsonCaptureTests | **7/7 passed** |
| В том числе SettingsSaveAdapter | **3/3 passed** |
| PlayMode, `EasySave.PlayMode.Tests` | **6/6 passed**, включая новый null capture сценарий |
| Исходные audit fixtures | **5 файлов, 0 изменений SHA256** |

Новые проверки подтверждают:

- Null reference DTO не создаёт slot/root и не вызывает WriteAsync/Validate/Restore; Capture вызван один раз.
- Несериализуемый Unity engine object также отклоняется до storage, с одним вызовом provider. Проверка JSON envelope является защитой границы сериализатора, а не новой семантической валидацией любого DTO.
- После корректных A/B две последовательные null-попытки не меняют primary/backup побайтно и не оставляют pending. Сначала успешно загружается B; после повреждения только уникального тестового primary восстанавливается A из сохранённого backup.
- Корректный Capture вызывается один раз без load validator; непустой DTO, marker `{}` и default struct проходят round trip.
- Null у последнего Preferences participant отклоняет весь документ, сохраняя прежний слот.
- Обычный ISaveParticipant с осмысленным пустым raw payload продолжает работать.
- PlayMode повторяет исходную границу: good save → null capture отказ → успешная загрузка прежнего primary, без fallback.

Settings adapter проверен существующими регрессиями confirmed graphics, сохранения реального окна после внешнего resize и отказа при preview/неизвестной версии. Его исходник не менялся. `tested-settings-dependency.json` фиксирует реально использованные исходники/assembly; это не утверждение о будущих одновременных правках Settings/Movement.

## Команды и воспроизведение

Подготовка — `Library/EasySaveWork/NullCaptureFix/Stage`, вне Unity import. Узкий runner `Library/EasySaveWork/NullCaptureFix/EditorStep.ps1` использует FileMode.CreateNew общей `Temp/UnityEditor.agent-lock`, уникального владельца/PID/UTC/operation и finally. Импортируемые записи выполнялись только после успешного lease; после тестов runner дождался остановленного Play Mode и завершения компиляции.

```powershell
& Library/EasySaveWork/NullCaptureFix/EditorStep.ps1 -Step Deploy
& Library/EasySaveWork/NullCaptureFix/EditorStep.ps1 -Step Edit
& Library/EasySaveWork/NullCaptureFix/EditorStep.ps1 -Step Play
```

Первый Deploy из sandbox получил `No Pipeline instance found` до изменения импортируемых файлов. Та же команда повторена с require_escalated; последующие обращения выполнялись к существующему Editor с тем же разрешённым доступом. Второй Editor не запускался.

Основные вызовы runner:

```text
unity command --result-only --project-path "C:\Users\LeshaPC\Games Factory" editor status
unity command --result-only --project-path "C:\Users\LeshaPC\Games Factory" script eval --code 'UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceSynchronousImport); return "refreshed";'
unity command --result-only --project-path "C:\Users\LeshaPC\Games Factory" script recompile
unity command --result-only --project-path "C:\Users\LeshaPC\Games Factory" script recompile_status
unity command --result-only --project-path "C:\Users\LeshaPC\Games Factory" test run --mode editor --filter EasySave --filter-type assembly --async-tests --timeout 180
unity command --result-only --project-path "C:\Users\LeshaPC\Games Factory" test run --mode playmode --filter EasySave.PlayMode.Tests --filter-type assembly --async-tests --timeout 180
unity command --result-only --project-path "C:\Users\LeshaPC\Games Factory" test status
```

Точные argument arrays, UTC каждого вызова и inventory сохранены в `Deploy-commands.json`, `Edit-commands.json`, `Play-commands.json` и соседних JSON. Полный code/contracts diff — `code-and-contracts.diff` (runtime, два новых test files, их новые metas, уточнение документации). Новые GUID относятся только к новым test files; существующие GUID не менялись.

## Область и ограничения

Пакет остаётся 0.1.0; формат, save schema, namespaces, существующие GUID и соседние пакеты не менялись. Объединение EasyGameFramework, публикация, push/tag и массовая миграция не выполнялись. Первоначальные audit fixtures сохранены; тесты писали только в уникальные собственные временные каталоги.

Этот проход проверяет локальный fix в Editor/PlayMode и затронутый adapter. Новый clean build, Player или проверенный UPM-архив в этом проходе не создавался; прежний архив 0.1.0 содержит прежний runtime и не является доказательством этого исправления. Не расширяются заявленные границы BuildingRecords, application validators Combat/Input/Movement/Building, idle Resources, платформ и partial restore.
