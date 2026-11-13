# Building + Feedbacks

Штатная integration сборка EasyBuilding.EasyFeedbacks. На BuildingSystem добавьте BuildingFeedbacks и назначьте пресеты Placed/Removed. Компонент подписывается на события и отписывается при выключении. Предпросмотр и восстановление снимка не воспроизводят событие новой установки.

ContextProvider необязателен: активный MonoBehaviour с IFeedbackContextProvider, обычно PlayerFeedbackContext из Input. PrepareContext сохраняет позицию/поворот/source/follow/intensity/audio/scale, явный TargetGamepad и explicit-none WithoutGamepad(). При отсутствии/выключении provider сохраняется исходное поведение. Для keyboard-only игрока используйте player provider, чтобы не подставился чужой Gamepad.current.

Отдельно опубликованный продукт EasyFeedbacks этим переносом не изменяется. Runtime/Editor/Tests этой integration входят в общий framework без sample import.