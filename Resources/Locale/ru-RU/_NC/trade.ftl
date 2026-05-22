ent-PrizeTicket = талон каравана
   .desc = Талон, используемый для обмена при помощи специального "торгового автомата". Позволяет заполучить довольно мощное оружие, если конечно хватит талончиков.
ent-PrizeTicket1 = { ent-PrizeTicket }
   .suffix = 1
   .desc = { ent-PrizeTicket.desc }
ent-PrizeTicket10  = { ent-PrizeTicket }
   .suffix = 10
   .desc = { ent-PrizeTicket.desc }
ent-PrizeTicket30  = { ent-PrizeTicket }
   .suffix = 30
   .desc = { ent-PrizeTicket.desc }
ent-PrizeTicket60  = { ent-PrizeTicket }
   .suffix = 60
   .desc = { ent-PrizeTicket.desc }
nc-store-window-title = Торговый Терминал
nc-store-select-category = Выберите категорию
nc-store-search-placeholder = Поиск товаров...
nc-store-footer-balance = Баланс:
nc-store-tab-buy = Покупка
nc-store-tab-sell = Продажа
nc-store-tab-contracts = Контракты
nc-store-cat-ready-short = Готово
nc-store-cat-crate-short = В ящике
nc-store-cat-ready-full = Готово к продаже
nc-store-cat-crate-full = Готово к продаже (в ящике)
nc-store-category-fallback = Разное
nc-store-mass-sell-button = Продать содержимое ящика
nc-store-mass-sell-tooltip = Опция для быстрой продажи всего содержимого.
    Условия:
    • Ящик должен быть закрыт
    • Вы должны тянуть ящик за собой
nc-store-mass-sell-tooltip-with-reward = { nc-store-mass-sell-tooltip }

    Оценочная стоимость: { $reward }
nc-store-only-mass-sell = Этот товар можно продать только оптом через закрытый ящик.
nc-store-show-more = Показать ещё ({ $count })
nc-store-prompt-select-category = Пожалуйста, выберите категорию слева.
nc-store-empty-search = По вашему запросу ничего не найдено.
nc-store-empty-category-search = В этой категории нет товаров, соответствующих запросу.
nc-store-search-results-buy = Результаты поиска (Покупка): { $count }
nc-store-search-results-sell = Результаты поиска (Продажа): { $count }
nc-store-no-stock = Нет в наличии
nc-store-buying-finished = Лимит исчерпан
nc-store-remaining = Остаток: { $count }
nc-store-in-stock = Есть в наличии: { $count }
nc-store-in-stock-unlimited = Есть в наличии
nc-store-will-buy = Требуется: { $count }
nc-store-owned = У вас есть: { $count }
nc-store-no-access = Ошибка доступа
nc-store-contracts-empty = Активных контрактов пока нет. Проверьте позже.
nc-store-difficulty-easy = Лёгкий
nc-store-difficulty-medium = Средний
nc-store-difficulty-hard = Сложный
nc-store-contract-title = Контракт ({ $difficulty })
nc-store-contract-badge-single = Разовый
nc-store-contract-badge-single-tooltip =
    Этот контракт доступен для выполнения только один раз за смену.
    После завершения он исчезнет из списка.
nc-store-contract-goals-header = Цели заказа:
nc-store-contract-turn-in-header = Сдать в автомат:
nc-store-contract-turn-in-note = После выполнения: { $item }
nc-store-contract-reward-header = Награда:
nc-store-contract-items-header = Предметы:
nc-store-contract-action-claim = Завершить контракт
nc-store-contract-action-claim-progress = Внести часть ({ $progress }/{ $required })
nc-store-contract-action-can-claim = Готово к сдаче
nc-store-contract-action-can-claim-proof = Готово, нужно сдать доказательство
nc-store-contract-action-not-taken = Не принят
nc-store-contract-action-not-done = Не выполнено
nc-store-contract-action-take = Взять контракт
nc-store-contract-confirm-take-title = Взятие контракта
nc-store-contract-confirm-skip-title = Пропуск контракта
nc-store-contract-confirm-take = Взять контракт «{ $contract }»?
nc-store-contract-confirm-skip = Пропустить контракт «{ $contract }» и заменить его новым?
nc-store-contract-confirm-take-action = Взять
nc-store-contract-confirm-skip-action = Пропустить
nc-store-contract-confirm-no = Нет
nc-store-contract-claim-tooltip-single = Завершить разовый контракт и получить полную награду.
nc-store-contract-claim-tooltip-repeatable = Сдать текущий результат по контракту и получить награду.
nc-store-contract-claim-tooltip-partial = Внести доступную часть груза. Награда будет выдана после полной сдачи.
nc-store-contract-claim-tooltip-not-done = Условия контракта ещё не выполнены. Недостаточно предметов.
nc-store-contract-take-tooltip = Принять контракт. После принятия его нельзя пропустить.
nc-store-contract-completed = Контракт успешно выполнен!
nc-store-contract-partial-turned-in = Часть груза принята.
nc-store-contract-taken = Контракт принят.
nc-store-contract-take-failed = Не удалось принять контракт.
nc-store-contract-goal-line = { $item }: { $count } шт.
nc-store-contract-progress-line = Прогресс выполнения: { $progress } из { $required }
nc-store-contract-progress-caption = Выполнение
nc-store-contract-progress-value = { $progress } / { $required }
nc-store-currency-format = { $amount } { $currency }
nc-store-contract-title-pretty = Контракт: { $difficulty } — { $goal }
nc-store-contract-title-pretty-nogoal = Контракт: { $difficulty }

nc-store-contract-desc-default = Выполните требования контракта и заберите награду.
nc-store-contract-desc-generated = Требуется: { $goals }

nc-store-contract-goal-inline = { $item } ×{ $count }

nc-store-unknown-item = ???

nc-store-proto-tooltip-name-only = { $name }
nc-store-proto-tooltip = { $name }
    { $desc }

nc-store-contract-reward-none = Награда не указана
nc-store-contract-reward-item-line = { $item } ×{ $count }

nc-store-contract-badge-taken = В РАБОТЕ
nc-store-contract-badge-taken-tooltip = Контракт уже у вас на руках. Снять его с доски больше нельзя.
nc-store-contract-badge-completed = ГОТОВ
nc-store-contract-badge-completed-tooltip = Работа сделана. Осталось сдать результат и забрать плату.

nc-store-contract-action-skip = Сменить ({ $cost } { $currency })
nc-store-contract-skip-tooltip =
    Снять этот контракт с доски и заменить его новым.
    Стоимость: { $cost } { $currency }.
nc-store-contract-skipped = Контракт снят. На его месте появился новый.
nc-store-contract-skip-failed = Не удалось сменить контракт. Не хватает средств.
nc-store-contract-skip-locked = Этот контракт уже у вас на руках. Снять его нельзя.


nc-store-contract-type-delivery = Доставка
nc-store-contract-type-delivery-tooltip = Обычный заказ на доставку нужного товара.

nc-store-contract-type-hunt = Контракт на голову
nc-store-contract-type-hunt-tooltip = После принятия появится цель. Уберите её, заберите доказательство и вернитесь за платой.

nc-store-contract-hunt-mode-trophy = Сдать трофей
nc-store-contract-hunt-mode-trophy-tooltip = После последнего нужного убийства появится финальный трофей. Его надо принести торговцу.
nc-store-contract-hunt-mode-trophy-desc = Доказательство: финальный трофей появится после последней цели. Заберите его и принесите торговцу.
nc-store-contract-hunt-mode-unknown = Особая охота
nc-store-contract-hunt-trophy-turn-in-header = Сдать трофей:
nc-store-contract-hunt-trophy-turn-in-note = После последней цели появится трофей: { $item }. Заберите его и принесите торговцу.
nc-store-contract-hunt-trophy-status-available = После принятия появятся контрактные цели.
nc-store-contract-hunt-trophy-status-progress = Убейте контрактные цели: { $progress }/{ $required }. Трофей ещё не появился.
nc-store-contract-hunt-trophy-status-ready = Трофей получен. Принесите его торговцу и завершите заказ.
nc-store-contract-hunt-trophy-action-available = Примите охоту.
nc-store-contract-hunt-trophy-action-progress = Цели: { $progress }/{ $required }. Трофей будет после последней.
nc-store-contract-hunt-trophy-action-ready = Сдайте трофей торговцу.

nc-store-contract-hunt-mode-body = Принести тело
nc-store-contract-hunt-mode-body-tooltip = Доказательством является тело отмеченной цели. Убейте цели и притащите нужное тело к торговцу.
nc-store-contract-hunt-mode-body-desc = Доказательство: тело отмеченной цели. После убийств притащите его к торговцу.
nc-store-contract-hunt-body-turn-in-header = Принести тело:
nc-store-contract-hunt-body-turn-in-note = После убийства притащите тело к торговцу: { $item }.
nc-store-contract-hunt-body-status-available = После принятия появятся контрактные цели. Отмеченное тело надо будет притащить торговцу.
nc-store-contract-hunt-body-status-progress = Убейте цели: { $progress }/{ $required }. Отмеченное тело не уничтожать: его надо принести.
nc-store-contract-hunt-body-status-ready = Все цели убиты. Притащите отмеченное тело к торговцу и завершите заказ.
nc-store-contract-hunt-body-action-available = Примите охоту.
nc-store-contract-hunt-body-action-progress = Цели: { $progress }/{ $required }. Сохраните тело.
nc-store-contract-hunt-body-action-ready = Притащите тело к торговцу.

nc-store-contract-type-repair = Ремонт
nc-store-contract-type-repair-tooltip = Заказ на восстановление объекта в несколько этапов. После ремонта нужно принести подтверждение работы.

nc-store-contract-type-ghost-role = Особая цель
nc-store-contract-type-ghost-role-tooltip = После принятия откроется особая роль. Если её займут, появится живая цель.
nc-store-contract-offer-pool-tooltip = Группа витрины: { $pool }
nc-store-contract-route-source-line = Загрузка: { $hint }
nc-store-contract-route-destination-line = Выгрузка: { $hint }
nc-store-contract-route-proof-bearer-note = После выгрузки появится накладная. Она предъявительская: награду получит тот, кто принесёт её торговцу.
nc-store-contract-duration-hours = { $count ->
    [one] { $count } час
    [few] { $count } часа
   *[other] { $count } часов
}
nc-store-contract-duration-minutes = { $count ->
    [one] { $count } минуту
    [few] { $count } минуты
   *[other] { $count } минут
}
nc-store-contract-duration-seconds = { $count ->
    [one] { $count } секунду
    [few] { $count } секунды
   *[other] { $count } секунд
}

nc-store-contract-runtime-stage = Этап: { $stage } из { $goal }

nc-store-contract-action-pinpointer = Выдать пеленгатор
nc-store-contract-action-pinpointer-tooltip = Выдать новый пеленгатор для текущей цели активного контракта.
nc-store-contract-pinpointer-issued = Пеленгатор выдан.
nc-store-contract-pinpointer-issue-failed = Не удалось выдать пеленгатор.
nc-store-contract-ghost-role-timeout = Никто не взял эту роль вовремя. Контракт сорван.
nc-store-contract-ghost-role-target-lost = Цель выбыла ещё до начала операции. Контракт сорван.
nc-store-contract-ghost-role-target-rotten = Цель сгнила. Контракт сорван.
nc-store-contract-ghost-role-survival-succeeded = Цель пережила отведённое время. Контракт сорван.
nc-store-contract-hunt-target-lost = Цель ликвидирована/утрачена до завершения всех этапов. Контракт сорван.
nc-store-contract-hunt-next-target-spawn-failed = Не удалось определить следующую цель для этапа охоты. Контракт сорван.

nc-store-contract-badge-awaiting-ghost-role = ОЖИДАНИЕ
nc-store-contract-badge-awaiting-ghost-role-tooltip = Идёт поиск исполнителя. Если за отведённое время никто не возьмётся за дело, контракт сорвётся.
nc-store-contract-badge-ghost-role-active = ЦЕЛЬ АКТИВНА
nc-store-contract-badge-ghost-role-active-tooltip = Цель уже в деле. Выполните условия заказа и доставьте её к торговому автомату.
nc-store-contract-ghost-role-waiting-line = Идёт поиск исполнителя: { $time }
nc-store-contract-ghost-role-waiting-action = Ожидание: { $time }
nc-store-contract-ghost-role-active-line = Цель вышла в поле. Выполните условия заказа и доставьте её к торговому автомату.
nc-store-contract-ghost-role-active-short = Цель активна
nc-store-contract-ghost-role-survival-line = До провала: { $time }.
nc-store-contract-ghost-role-survival-action = Таймер: { $time }
nc-store-contract-ghost-role-mode-alive = Сдать живым
nc-store-contract-ghost-role-mode-alive-hint = Цель должна быть жива, без обычных повреждений, в наручниках, рядом с торговцем и без гниения.
nc-store-contract-ghost-role-mode-dead = Сдать мёртвым
nc-store-contract-ghost-role-mode-dead-hint = Доставьте тело цели к торговцу до гниения. Если тело сгниёт или будет уничтожено, контракт сорвётся.
nc-store-contract-ghost-role-mode-unknown = Особая сдача
nc-store-contract-ghost-role-mode-line = Режим: { $mode }. { $hint }
nc-store-contract-ghost-role-mode-line-short = Режим: { $mode }.
nc-store-contract-ghost-role-hint-waiting = Ожидайте, пока кто-нибудь примет особую роль.
nc-store-contract-ghost-role-hint-deliver = Доставьте цель к торговцу.
nc-store-contract-ghost-role-hint-alive-revive = Цель мертва. Для этого заказа её нужно оживить, вылечить обычные повреждения и заковать в наручники.
nc-store-contract-ghost-role-hint-alive-cuff = Цель жива, но не в наручниках. Наденьте наручники.
nc-store-contract-ghost-role-hint-alive-heal = Цель ранена. Вылечите обычные повреждения перед сдачей.
nc-store-contract-ghost-role-hint-alive-ready = Цель жива, без обычных повреждений и в наручниках. Можно сдавать контракт.
nc-store-contract-ghost-role-hint-dead-kill = Для этого заказа нужно тело цели. Убейте цель и доставьте тело торговцу.
nc-store-contract-ghost-role-hint-dead-deliver = Тело цели готово. Доставьте его к торговцу до гниения.
nc-store-contract-ghost-role-hint-dead-ready = Тело доставлено. Можно сдавать контракт.
nc-store-contract-ghost-role-character-briefing = Контрактная роль: { $contract }.
    { $description }
nc-store-contract-ghost-role-character-briefing-survival = Контрактная роль: { $contract }.
    { $description }
    { $survival }
nc-store-contract-ghost-role-survival-briefing = Выживите { $time }. Если вы продержитесь до конца, контракт охотников будет сорван.
nc-store-contract-ghost-role-survival-objective-title = Выжить: { $contract }
nc-store-contract-ghost-role-survival-objective-title-live = Выжить: { $time }
nc-store-contract-ghost-role-survival-objective-title-done = Выживание выполнено
nc-store-contract-ghost-role-survival-objective-description = Продержитесь { $time }. Если вы выживете, контракт против вас будет провален.
nc-store-contract-ghost-role-roundend-header = [bold]Итоги контрактных целей[/bold]
nc-store-contract-ghost-role-roundend-line = - Контракт «{ $contract }»: { $role } ({ $player }) — { $result }
nc-store-contract-ghost-role-roundend-unknown-role = неизвестная цель
nc-store-contract-ghost-role-roundend-no-player = роль не занята
nc-store-contract-ghost-role-roundend-result-waiting = роль никто не занял
nc-store-contract-ghost-role-roundend-result-active = цель не была сдана до конца раунда
nc-store-contract-ghost-role-roundend-result-delivered-alive = цель сдана живой; контракт выполнен
nc-store-contract-ghost-role-roundend-result-delivered-dead = цель убита/сдана; контракт выполнен
nc-store-contract-ghost-role-roundend-result-survived = цель пережила таймер ({ $time }); контракт сорван
nc-store-contract-ghost-role-roundend-result-not-accepted = роль никто не занял; контракт сорван
nc-store-contract-ghost-role-roundend-result-target-lost = цель была утрачена; контракт сорван
nc-store-contract-ghost-role-roundend-result-target-rotten = тело цели сгнило; контракт сорван


nc-store-contract-delivery-target-lost = Груз утрачен. Контракт сорван.
nc-store-contract-proof-generation-failed = Подтверждение выполнения не сформировалось. Контракт сорван.
nc-store-contract-proof-destroyed = Доказательство для контракта было уничтожено, контракт провален.

nc-store-contract-title-fallback = Контракт
nc-store-contract-progress-caption-delivered = Доставлено
nc-store-contract-route-status-available = Маршрут ещё не принят.
nc-store-contract-route-status-progress = Доставлено груза: { $progress } / { $max }.
nc-store-contract-route-status-delivered = Груз доставлен. Завершите маршрут.
nc-store-contract-route-status-find-cargo = Найдите груз и доставьте его по маршруту.
nc-store-contract-route-status-proof-bearer = Доставка подтверждена. Верните доказательство торговцу; награду получит предъявитель.
nc-store-contract-route-status-proof-return = Доставка подтверждена. Вернитесь к торговцу с доказательством.
nc-store-contract-route-status-store-cargo-ready = Груз доставлен. Заберите награду у торговца.
nc-store-contract-route-status-ready = Маршрут выполнен. Получите награду у торговца.
nc-store-contract-route-action-available = Примите маршрут доставки.
nc-store-contract-route-action-progress = Доставьте груз: { $progress } / { $max }.
nc-store-contract-route-action-proof-after-delivery = После полной сдачи получите одно доказательство доставки.
nc-store-contract-route-action-wait-confirmation = Дождитесь подтверждения доставки.
nc-store-contract-route-action-proof-bearer = Принесите доказательство торговцу. Его можно передать, украсть или продать.
nc-store-contract-route-action-proof = Принесите доказательство торговцу.
nc-store-contract-route-action-store-cargo-ready = Награда доступна у торговца. Proof не нужен.
nc-store-contract-route-action-ready = Получите награду у торговца.
