## Quest Instance System

# Popup when join window has expired and new player tries to enter
quest-instance-join-closed = Вход в зону закрыт — окно входа ({ $seconds } сек.) уже истекло.

# Popup when instance reached time limit and is being closed
quest-instance-expired = Зона уже закрывается. Подождите открытия нового задания.

# Warning popups sent before force-close
quest-instance-warning = Зона закроется через { $seconds } секунд. Найдите указатель выхода!

## Quest Board BUI (QuestBoardWindow)

quest-board-title = Доска заданий

# Status line at the top of the window
quest-board-status-idle = Активных заданий нет. Выберите сложность.
quest-board-status-active =
    Активная зона • { $players ->
        [one]   { $players } игрок
        [few]   { $players } игрока
        [many]  { $players } игроков
       *[other] { $players } игроков
    } • осталось { $minutes }:{ NUMBER($seconds, minimumIntegerDigits: 2) }

# Section header above difficulty buttons
quest-board-select-difficulty = Выберите сложность:

# Difficulty buttons
quest-board-easy   = Лёгкое  (15 мин)
quest-board-medium = Среднее (20 мин)
quest-board-hard   = Тяжёлое (25 мин)

# Label and button shown when an instance is already active
quest-board-instance-active = Зона уже открыта — присоединиться?
quest-board-join = Войти в зону
