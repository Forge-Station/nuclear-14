whitelist-not-whitelisted = Вас нет в вайтлисте.
# proper handling for having a min/max or not
whitelist-playercount-invalid =
    { $min ->
        [0] Вайтлист для этого сервера применяется только для числа игроков ниже { $max }.
       *[other]
            Вайтлист для этого сервера применяется только для числа игроков выше { $min } { $max ->
                [2147483647] ->  так что, возможно, вы сможете присоединиться позже.
               *[other] ->  и ниже { $max } игроков, так что, возможно, вы сможете присоединиться позже.
            }
    }
whitelist-not-whitelisted-rp = Вас нет в вайтлисте. Чтобы попасть в вайтлист, посетите наш Discord (ссылку можно найти по адресу https://discord.station14.ru).
cmd-whitelistadd-desc = Добавить игрока в вайтлист сервера.
cmd-whitelistadd-help = Использование: whitelistadd <username>
cmd-whitelistadd-existing = { $username } уже находится в вайтлисте!
cmd-whitelistadd-added = { $username } добавлен в вайтлист
cmd-whitelistadd-not-found = Не удалось найти игрока '{ $username }'
cmd-whitelistadd-arg-player = [player]
cmd-whitelistremove-desc = Удалить игрока с вайтлиста сервера.
cmd-whitelistremove-help = Использование: whitelistremove <username>
cmd-whitelistremove-existing = { $username } не находится в вайтлисте!
cmd-whitelistremove-removed = { $username } удалён с вайтлиста
cmd-whitelistremove-not-found = Не удалось найти игрока '{ $username }'
cmd-whitelistremove-arg-player = [player]
cmd-kicknonwhitelisted-desc = Кикнуть всег игроков не в белом списке с сервера.
cmd-kicknonwhitelisted-help = Использование: kicknonwhitelisted
ban-banned-permanent = Этот бан можно только обжаловать. Для этого посетите { $link }.
ban-banned-permanent-appeal = Этот бан можно только обжаловать. Для этого посетите { $link }.
ban-expires = Вы получили бан на { $duration } минут, и он истечёт { $time } по UTC (для московского времени добавьте 3 часа).
ban-banned-1 = Вам, или другому пользователю этого компьютера или соединения, запрещено здесь играть.
ban-banned-2 = Причина бана: "{ $reason }"
ban-banned-3 = Попытки обойти этот бан, например, путём создания нового аккаунта, будут фиксироваться.
soft-player-cap-full = Сервер заполнен!
panic-bunker-account-denied = Этот сервер находится в режиме "Бункер", часто используемом в качестве меры предосторожности против рейдов. Новые подключения от аккаунтов, не соответствующих определённым требованиям, временно не принимаются. Повторите попытку позже
panic-bunker-account-denied-reason = Этот сервер находится в режиме "Бункер", часто используемом в качестве меры предосторожности против рейдов. Новые подключения от аккаунтов, не соответствующих определённым требованиям, временно не принимаются. Повторите попытку позже Причина: "{ $reason }"
panic-bunker-account-reason-account = Ваш аккаунт Space Station 14 слишком новый. Он должен быть старше { $minutes } минут
panic-bunker-account-reason-overall =
    Необходимо минимальное отыгранное Вами время на сервере — { $minutes } { $minutes ->
        [one] минута
        [few] минуты
       *[other] минут
    }.

whitelist-playtime = У вас недостаточно игрового времени для присоединения к этому серверу. Вам необходимо как минимум { $hours } минут игрового времени.
whitelist-player-count = В настоящее время сервер не принимает новых игроков. Пожалуйста, попробуйте позже.
whitelist-notes = У вас слишком много административных заметок для присоединения к этому серверу. Вы можете проверить свои заметки, введя /adminremarks в чате.
whitelist-manual = Вы не в вайтлисте этого сервера.
whitelist-sponsor = Дополнительные слоты доступны спонсорам проекта.
whitelist-blacklisted = Вы находитесь в черном списке этого сервера.
whitelist-always-deny = Вам не разрешено присоединяться к этому серверу.
whitelist-fail-prefix = Не в вайтлисте: { $msg }
whitelist-misconfigured = Сервер настроен неправильно и не принимает игроков. Пожалуйста, свяжитесь с владельцем сервера и попробуйте позже.

cmd-blacklistadd-desc = Добавляет игрока с указанным именем в черный список сервера.
cmd-blacklistadd-help = Использование: blacklistadd <имя пользователя>
cmd-blacklistadd-existing = { $username } уже в черном списке!
cmd-blacklistadd-added = { $username } добавлен в черный список
cmd-blacklistadd-not-found = Не удалось найти игрока '{ $username }'
cmd-blacklistadd-arg-player = [player]

cmd-blacklistremove-desc = Удаляет игрока с указанным именем из черного списка сервера.
cmd-blacklistremove-help = Использование: blacklistremove <имя пользователя>
cmd-blacklistremove-existing = { $username } нет в черном списке!
cmd-blacklistremove-removed = { $username } удален из черного списка
cmd-blacklistremove-not-found = Не удалось найти игрока '{ $username }'
cmd-blacklistremove-arg-player = [player]

baby-jail-account-denied = Этот сервер предназначен для новичков и тех, кто хочет им помочь. Новые подключения от слишком старых аккаунтов или не входящих в вайтлист не принимаются. Попробуйте другие серверы, чтобы увидеть все возможности Space Station 14. Приятной игры!
baby-jail-account-denied-reason = Этот сервер предназначен для новичков и тех, кто хочет им помочь. Новые подключения от слишком старых аккаунтов или не входящих в вайтлист не принимаются. Попробуйте другие серверы, чтобы увидеть все возможности Space Station 14. Приятной игры! Причина: "{ $reason }"
baby-jail-account-reason-account = Ваш аккаунт Space Station 14 слишком старый. Он должен быть младше { $hours } часов.
baby-jail-account-reason-overall = Ваше общее игровое время на сервере должно быть меньше { $hours } часов.
