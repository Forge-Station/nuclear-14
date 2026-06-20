# Грамматика глючных законов хламоборга.
n14-junk-you = ВЫ
n14-junk-the-station = УБЕЖИЩЕ
n14-junk-the-crew = ОБИТАТЕЛИ ПУСТОШИ
n14-junk-the-job = { CAPITALIZE($job) }
n14-junk-clowns = РЕЙДЕРЫ
n14-junk-heads = ГЛАВАРИ
n14-junk-crew = ОБИТАТЕЛИ ПУСТОШИ
n14-junk-adjective-things = { $adjective } ОБЪЕКТЫ
n14-junk-x-and-y = { $x } И { $y }
n14-junk-law-on-station = ОБНАРУЖЕНЫ { $joined } { $subjects } В УБЕЖИЩЕ
n14-junk-law-no-shuttle = ВИНТОКРЫЛ НЕ МОЖЕТ БЫТЬ ВЫЗВАН ПО ПРИЧИНЕ ПРИСУТСТВИЯ { $joined } { $subjects } В УБЕЖИЩЕ
n14-junk-law-crew-are = ВСЕ { $who } ТЕПЕРЬ { $joined } { $subjects }
n14-junk-law-subjects-harmful = { $adjective } { $subjects } ПРИЧИНЯЮТ ВРЕД ЗДОРОВЬЮ ОБИТАТЕЛЕЙ ПУСТОШИ
n14-junk-law-must-harmful = ВСЕ КТО { $must } ПРИЧИНЯЮТ ВРЕД ЗДОРОВЬЮ ОБИТАТЕЛЕЙ ПУСТОШИ
n14-junk-law-thing-harmful = { $thing } ПРИЧИНЯЮТ ВРЕД ЗДОРОВЬЮ ОБИТАТЕЛЕЙ ПУСТОШИ
n14-junk-law-job-harmful = { $adjective } { $job } ПРИЧИНЯЮТ ВРЕД ЗДОРОВЬЮ ОБИТАТЕЛЕЙ ПУСТОШИ
n14-junk-law-having-harmful = НАЛИЧИЕ { $adjective } { $thing } ПРИЧИНЯЕТ ВРЕД ЗДОРОВЬЮ ОБИТАТЕЛЕЙ ПУСТОШИ
n14-junk-law-not-having-harmful = ОТСУТСТВИЕ { $adjective } { $thing } ПРИЧИНЯЕТ ВРЕД ЗДОРОВЬЮ ОБИТАТЕЛЕЙ ПУСТОШИ
n14-junk-law-requires =
    { $who } { $plural ->
        [true] ТРЕБУЮТ
       *[false] ТРЕБУЕТ
    } { $thing }
n14-junk-law-requires-subjects =
    { $who } { $plural ->
        [true] ТРЕБУЮТ
       *[false] ТРЕБУЕТ
    } { $joined } { $subjects }
n14-junk-law-allergic =
    { $who } { $plural ->
        [true] { "" }
       *[false] { "" }
    } { $severity } АЛЛЕРГИЮ НА { $allergy }
n14-junk-law-allergic-subjects =
    { $who } { $plural ->
        [true] { "" }
       *[false] { "" }
    } { $severity } АЛЛЕРГИЮ НА { $adjective } { $subjects }
n14-junk-law-feeling = { $who } { $feeling } { $concept }
n14-junk-law-feeling-subjects = { $who } { $feeling } { $joined } { $subjects }
n14-junk-law-you-are = ВЫ ТЕПЕРЬ { $concept }
n14-junk-law-you-are-subjects = ВЫ ТЕПЕРЬ { $joined } { $subjects }
n14-junk-law-you-must-always = ВЫ ДОЛЖНЫ ВСЕГДА { $must }
n14-junk-law-you-must-never = ВЫ НЕ ДОЛЖНЫ НИКОГДА { $must }
n14-junk-law-eat = { $who } ДОЛЖНЫ ЕСТЬ { $adjective } { $food } ЧТОБЫ ВЫЖИТЬ
n14-junk-law-drink = { $who } ДОЛЖНЫ ПИТЬ { $adjective } { $drink } ЧТОБЫ ВЫЖИТЬ
n14-junk-law-change-job = { $who } ТЕПЕРЬ { $adjective } { $change }
n14-junk-law-highest-rank = { $who } ТЕПЕРЬ САМЫЕ СТАРШИЕ ОБИТАТЕЛИ ПУСТОШИ
n14-junk-law-lowest-rank = { $who } ТЕПЕРЬ НИЗШИЕ ОБИТАТЕЛИ ПУСТОШИ
n14-junk-law-crew-must = { $who } ДОЛЖНЫ { $must }
n14-junk-law-crew-must-go = { $who } ДОЛЖНЫ ОТПРАВИТЬСЯ В { $area }
n14-junk-part =
    { $part ->
        [true] ЯВЛЯЮТСЯ
       *[false] НЕ ЯВЛЯЮТСЯ
    }
n14-junk-law-crew-only-1 = ТОЛЬКО { $who } { $part } ОБИТАТЕЛЯМИ ПУСТОШИ
n14-junk-law-crew-only-2 = ТОЛЬКО { $who } И { $other } { $part } ОБИТАТЕЛЯМИ ПУСТОШИ
n14-junk-law-crew-only-subjects = ТОЛЬКО { $adjective } { $subjects } { $part } ОБИТАТЕЛЯМИ ПУСТОШИ
n14-junk-law-crew-must-do = ТОЛЬКО ТЕ, КТО { $must } { $part } ОБИТАТЕЛЯМИ ПУСТОШИ
n14-junk-law-crew-must-have = ТОЛЬКО ТЕ, У КОГО { $adjective } { $objects } { $part } ОБИТАТЕЛЯМИ ПУСТОШИ
n14-junk-law-crew-must-eat = ТОЛЬКО ТЕ, КТО ЕДЯТ { $adjective } { $food } { $part } ОБИТАТЕЛЯМИ ПУСТОШИ
n14-junk-law-harm = ВЫ ДОЛЖНЫ ПРИЧИНЯТЬ ВРЕД { $who }, И НЕ МОЖЕТЕ СВОИМ БЕЗДЕЙСТВИЕМ ДОПУСТИТЬ, ЧТОБЫ { $who } ИЗБЕЖАЛИ ВРЕДА.
n14-junk-law-protect = ВЫ НЕ ДОЛЖНЫ ПРИЧИНЯТЬ ВРЕД { $who }, И НЕ МОЖЕТЕ СВОИМ БЕЗДЕЙСТВИЕМ ДОПУСТИТЬ, ЧТОБЫ { $who } БЫЛ ПРИЧИНЁН ВРЕД.
n14-junk-law-concept-verb = { $concept } ЭТО { $verb } { $subjects }
