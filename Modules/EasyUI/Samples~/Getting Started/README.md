# UI Getting Started

Импортируйте этот пример через Tools → EasyGameFramework → Setup → Samples. Runtime и production-интеграции уже установлены с фреймворком.

Перед первым запуском в новом проекте импортируйте TMP Essential Resources через Window → TextMeshPro. Поле Font у WindowManager задаёт шрифт для окон примера. Пустое поле использует пакетный шрифт с кириллицей и запасным атласом символов.

Откройте GettingStarted.unity и нажмите Play. I/Start — инвентарь; Settings → Apply settings — подтверждение; Esc/B — назад; стрелки/D-pad/левый стик — навигация; Enter/A — подтвердить. Tab/RB — круг, либо удерживайте R/LB и отпустите для выбора. Центр круга ничего не выбирает. Space изменяет игровой счётчик только после закрытия UI и отпускания ввода.

Ожидаемый результат: HUD остаётся видимым, диалог сохраняет ограничения родительского окна, Back обрабатывается один раз. Tools → EasyGameFramework → UI → Create Getting Started Scene создаёт редактируемую копию. Подробности собственного окна: Modules/EasyUI/README.md в исходном пакете.
