character-age-requirement =
    Вам { $inverted ->
        [true] не следует быть
       *[other] следует быть
    } в возрасте от [color=yellow]{ $min }[/color] до [color=yellow]{ $max }[/color] лет
character-species-requirement =
    Вам { $inverted ->
        [true] не следует быть
       *[other] следует быть
    } [color=green]{ $species }[/color]
character-trait-requirement =
    Вам { $inverted ->
        [true] не следует иметь
       *[other] следует иметь
} одну из черт: [color=lightblue]{$traits}[/color]
character-backpack-type-requirement =
    Вам { $inverted ->
        [true] не следует использовать
       *[other] следует использовать
    } [color=lightblue]{ $type }[/color] в качестве вашей сумки
character-clothing-preference-requirement =
    Вам { $inverted ->
        [true] не следует носить
       *[other] следует носить
    } [color=lightblue]{ $type }[/color]
character-job-requirement =
    Вы должны { $inverted ->
        [true] не быть
       *[other] быть
    } одним из этих профессий: { $jobs }
character-department-requirement =
    Вы должны { $inverted ->
        [true] не быть
       *[other] быть
    } в одном из этих отделов: { $departments }
character-timer-department-insufficient = Вам необходимо [color=yellow]{ TOSTRING($time, "0") }[/color] минут дополнительно в отделе [color={ $departmentColor }]{ $department }[/color]
character-timer-department-too-high = Вам необходимо [color=yellow]{ TOSTRING($time, "0") }[/color] минут меньше в отделе [color={ $departmentColor }]{ $department }[/color]
character-timer-overall-insufficient = Вам необходимо [color=yellow]{ TOSTRING($time, "0") }[/color] минут дополнительно общего времени игры
character-timer-overall-too-high = Вам необходимо [color=yellow]{ TOSTRING($time, "0") }[/color] минут меньше общего времени игры
character-timer-role-insufficient = Вам необходимо [color=yellow]{ TOSTRING($time, "0") }[/color] минут дополнительно в роли [color={ $departmentColor }]{ $job }[/color]
character-timer-role-too-high = Вам необходимо [color=yellow]{ TOSTRING($time, "0") }[/color] минут меньше в роли [color={ $departmentColor }]{ $job }[/color]
character-trait-group-exclusion-requirement = Вы не можете выбрать это, если у вас есть одна из следующих черт: { $traits }
character-loadout-group-exclusion-requirement = Вы не можете выбрать это, если у вас есть один из следующих наборов снаряжения: { $loadouts }
character-gender-requirement =
    Вам { $inverted ->
        [true] не следует иметь
       *[other] следует иметь
    } местоимения [color=white]{$gender}[/color]

character-sex-requirement =
    Вам { $inverted ->
        [true] не следует быть
       *[other] следует быть
    } [color=white]{$sex ->
        [None] без пола
       *[other] {$sex}
    }[/color]

character-height-requirement =
    Вам { $inverted ->
        [true] не следует быть
       *[other] следует быть
    } таким по росту, что:
    – если указан только максимум, то короче [color={$color}]{$max}[/color] см;
    – если указан только минимум, то выше [color={$color}]{$min}[/color] см;
    – если заданы и минимум, и максимум, то между [color={$color}]{$min}[/color] и [color={$color}]{$max}[/color] см.

character-width-requirement =
    Вам { $inverted ->
        [true] не следует быть
       *[other] следует быть
    } таким по ширине, что:
    – если указан только максимум, то стройнее (тоньше) чем [color={$color}]{$max}[/color] см;
    – если указан только минимум, то шире чем [color={$color}]{$min}[/color] см;
    – если заданы и минимум, и максимум, то между [color={$color}]{$min}[/color] и [color={$color}]{$max}[/color] см.

character-weight-requirement =
    Вам { $inverted ->
        [true] не следует быть
       *[other] следует быть
    } таким по весу, что:
    – если указан только максимум, то легче, чем [color={$color}]{$max}[/color] кг;
    – если указан только минимум, то тяжелее, чем [color={$color}]{$min}[/color] кг;
    – если заданы и минимум, и максимум, то между [color={$color}]{$min}[/color] и [color={$color}]{$max}[/color] кг.

character-loadout-requirement =
    Вам { $inverted ->
        [true] не следует иметь
       *[other] следует иметь
    } один из следующих наборов снаряжения: {$loadouts}

character-item-group-requirement =
    Вам { $inverted ->
        [true] следует иметь {$max} или более
       *[other] следует иметь {$max} или меньше
    } предметов из группы [color=white]{$group}[/color]

character-logic-and-requirement-listprefix = {$indent}[color=gray]&[/color]{ " " }

character-logic-and-requirement =
    Вам { $inverted ->
        [true] не следует подходить
       *[other] следует подходить
    } под все следующие требования: {$options}

character-logic-or-requirement-listprefix = {$indent}[color=white]O[/color]{ " " }

character-logic-or-requirement =
    Вам { $inverted ->
        [true] не следует подходить
       *[other] следует подходить
    } под хотя бы одно из следующих требований: {$options}

character-logic-xor-requirement-listprefix = {$indent}[color=white]X[/color]{ " " }

character-logic-xor-requirement =
    Вам { $inverted ->
        [true] не следует подходить
       *[other] следует подходить
    } ровно под одно из следующих требований: {$options}

character-whitelist-requirement =
    Вам { $inverted ->
        [true] не следует быть
       *[other] следует быть
    } включённым в белый список

character-minPlayers-requirement = Данная роль доступна только от {$min} человек в сети.
character-sponsor-requirement = Необходимый уровень поддержки сервера для разблокировки: {$level}
