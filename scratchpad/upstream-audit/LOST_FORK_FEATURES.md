# Фичи форка, потерянные при апстриме b20a6845 (15.10.2026)

База мерджа: d68fa97f (10.09.2023). Расхождение ~3 года.

## Требуют повторного порта

### 1. Shitmed (хирургия, конечности, таргетинг) — 467 файлов
Апстрим влил Nubody (#42419, 16.01.2026): удалены `Content.Shared/Body/Part`,
`Body/Organ`, `SharedBodySystem.{Body,Parts,Organs}`, `BodyPartComponent`.
Новая архитектура — граф органов: `OrganComponent`, `ParentOrganComponent`,
`ChildOrganComponent`, `OrganRelationSystem`, `VisualBodyComponent`.
`BodySystem.Compatibility.cs` даёт только obsolete `TryGetOrgansWithComponent`.
РЕШЕНИЕ: портировать Shitmed на Nubody.

### 2. Humanoid-кастомизация
Апстрим переписал: `HumanoidAppearanceComponent` -> `HumanoidProfileComponent`,
`SharedHumanoidAppearanceSystem` -> `HumanoidProfileSystem`, `SkinColor.cs` удалён.

### 3. Corvax auto-eat / quick-swap
Мёртвые cvar'ы без потребителей после мерджа (код-потребитель удалён апстримом):
- `game.auto_eat_food`, `game.auto_eat_drinks` (был Content.Server/Nutrition/EntitySystems/FoodSystem.cs — удалён апстримом)
- `game.slot_quick_swap`
- `admin.bwoink_use_admin_ooc_color`
- `weather.auto`
cvar-определения не переносились. Нужен повторный порт поведения.

## Сохранено вручную
- `ui.lobby_new_vegas_theme` (LobbyNewVegasTheme) — потребитель жив (Lobby UI форка)
- `accessibility.no_vision_filters` (NoVisionFilters) — потребитель жив (DogVisionSystem)
- `game.defaultpreset` = "N14" (апстрим: "secret")
- `viewport.maximum_width` = 30 (апстрим: 21)
- `vote.restart_enabled` = false (апстрим: true)

## Референсы
- `manual_224_files.txt` — 224 файла с форк-маркерами, разбираемые вручную
- `fork_changes_224.patch` — форк-изменения в этих 224 файлах (vs база мерджа)
- `fork-side-224/` — полные форк-версии этих 224 файлов
- `LOST_fork_changes.patch` (53 МБ) — форк-изменения в 4725 авторазрешённых файлах
- `UD_mapping.txt` — карта переносов удалённых апстримом файлов

## Выброшено при разборе 224 ручных конфликтов (апстрим переписал окружающую логику)

| Фича | Файлы | Почему |
|---|---|---|
| DeltaV: импорт действий из YamlStream (`LoadActionAssignments(YamlStream)`) | Content.Client/Actions/ActionsSystem.cs, Content.Client/Commands/ActionsCommands.cs | апстрим переписал загрузчик действий (mapping-действия, EntProtoId) |
| Goobstation: переделка модсьютов (мультислотовые `ClothingPrototypes`/`ClothingUids`) | Clothing/Components/{Attached,Toggleable}ClothingComponent.cs | системная часть (`ToggleableClothingSystem`) уже вытеснена апстримом при авторазрешении; `Content.Client/_Goobstation/Clothing/ToggleableClothingRadialMenu.xaml.cs` теперь без данных |
| Goobstation: переделка мехов (руки/виртуальные предметы, стрельба вне меха, фонари, emag, фикс scram) | Mech/EntitySystems/SharedMechSystem.cs, Mech/Components/MechComponent.cs | апстрим переписал меха: нет `TryInsert`/`TryEject`, нет обработчика `EntGotRemovedFromContainerMessage` — поблочно не привязывается. `HandleMechEquipmentBatteryEvent` спасён в `Content.Shared/_Goobstation/Mech/MechEquipmentBatteryEvents.cs`, но его больше никто не поднимает |
| Goobstation: `PickRandomPersonComponent.NeedsOrganic` | Objectives/Components/PickRandomPersonComponent.cs | ни один потребитель не использовал |
| Goobstation: `GetAliveHumans(excludeSilicon:)` | Mind/SharedMindSystem.cs | ни один вызов не передавал `true`; апстрим вынес логику в `AddAliveHumans` |
| Goobstation: закомментированный `OnProjectileHit` для ensnare | Ensnaring/SharedEnsnareableSystem.cs | код был закомментирован и в форке |
| Corvax: `using Content.Shared.Crafting.Events` | UserInterface/Systems/Storage/StorageUIController.cs | каталог `Content.Shared/Crafting` в форке отсутствует — мёртвый using |
| Corvax: хардкод RU-строки «Поиск...» | Construction/UI/ConstructionMenu.xaml | апстрим переделал окно; строку надо вести во FTL |

## Спасённые в форк-овые файлы типы (их дом удалён апстримом)
- `TtsDefaults.DefaultVoice` / `DefaultSexVoice` -> `Content.Shared/_NC/TTS/TtsDefaults.cs`
- `BarkDefaults.DefaultBarkVoice` -> `Content.Shared/_NC/Barks/BarkDefaults.cs`
- `HandleMechEquipmentBatteryEvent` -> `Content.Shared/_Goobstation/Mech/MechEquipmentBatteryEvents.cs`

## Типы, всё ещё не имеющие дома (нужно решить при сборке)
- `GhostBarSpawnEvent` (Goobstation Ghost Bar) — жил в удалённом `Content.Shared/Ghost/SharedGhostSystem.cs`
- `GhostReturnToRoundRequest` — там же; потребитель `Content.Server/Ghost/GhostReturnToRoundSystem.cs`
- `AgentIDCardBoundUserInterfaceState`, `AgentIDCardNumberChangedMessage` (DeltaV) — жили в удалённом `Content.Shared/Access/SharedAgentIDCardSystem.cs`
- `AfterGeneralRecordCreatedEvent` — жил в удалённом `Content.Server/StationRecords/Systems/StationRecordsSystem.cs`; потребители в `Content.Server/PsionicsRecords/`

## Отложено на порт Humanoid/лобби (взят чистый апстрим)

Апстрим переписал кастомизацию (`HumanoidAppearanceComponent` -> `HumanoidProfileComponent`,
`SkinColor` удалён, `SkinColorationPrototype`+Strategy вместо enum `HumanoidSkinColor`),
поэтому UI лобби всё равно переписывается. Взята апстрим-версия этих файлов:

- `Content.Client/Lobby/UI/HumanoidProfileEditor.xaml{,.cs}`
- `Content.Client/Lobby/UI/CharacterSetupGui.xaml{,.cs}`
- `Content.Client/Lobby/UI/LobbyGui.xaml{,.cs}`
- `Content.Client/Lobby/LobbyUIController.cs`
- `Content.Client/UserInterface/Systems/Character/CharacterUIController.cs`
- `Content.Client/Options/UI/Tabs/AudioTab.xaml{,.cs}`

Вернуть портом:
1. Спонсорские косметические настройки в редакторе и лобби (цвет OOC/LOOC, скин призрака),
   кнопка/окно спонсора. Данные уже сохранены: `PlayerPreferences.Sponsor{OOCColor,LOOCColor,GhostSkin}`.
2. Выбор TTS-голоса и барк-голоса. Данные сохранены: `HumanoidCharacterProfile.TtsVoice`, `.BarkVoice`.
   ВНИМАНИЕ: форк-овое поле `Voice` переименовано в `TtsVoice`, потому что апстрим занял имя `Voice`
   под `ProtoId<EmoteSoundsPrototype>` (звуки эмоций). Потребители: `Content.Client/_NC/TTS/TTSTab.xaml.cs`,
   `Content.Client/_NC/Barks/HumanoidProfileEditor.Barks.cs`, `ServerDbBase`, `Model.cs`.
3. Ползунки громкости TTS/барков в AudioTab (`ContentAudioSystem.TtsMultiplier`, `.BarksMultiplier` сохранены).
4. Тема New Vegas в лобби (cvar `ui.lobby_new_vegas_theme` сохранён).
5. Форк-овые partial-классы редактора (`_NC/Barks/HumanoidProfileEditor.Barks.cs`) рассчитывают на
   контролы `BarkVoiceButton`, `BarkVoicePlayButton` — их нужно вернуть в XAML редактора.

## Прочее, отложенное на порт
- Требования ролей форка (`CharacterRequirementsSystem` + спонсоры) в поздней подсадке и окне ролей призрака:
  апстрим перешёл на `JobRequirementsManager.IsAllowed`. Файлы: `Content.Client/LateJoin/LateJoinGui.cs`,
  `Content.Client/UserInterface/Systems/Ghost/Controls/Roles/GhostRolesEui.cs`.
  Серверная сторона (`TraitSystem`, `ThiefUndeterminedBackpackSystem`) оставлена форк-овой.
- ru-RU FTL-ключи админ-вкладок надо перенаправить на апстримные имена
  (`admin-ui-atmos-*`, `administration-ui-round-tab-*-round`, `player-tab-player-count`).

## Ещё отложено на порт (апстрим переписал окружение)

| Фича | Файлы | Что уцелело / что нужно |
|---|---|---|
| Forge: выбор языка при письме на бумаге | `Content.Client/Paper/UI/PaperBoundUserInterface.cs`, `PaperWindow.xaml{,.cs}` | апстрим перенёс Paper из Server в Shared и переименовал `SharedPaperComponent` -> `PaperComponent`; у `PaperInputTextMessage` нет параметра языка. Форк-овый `Content.Client/Paper/PaperLanguageVisualsSystem.cs` жив — нужно вернуть перегрузку сообщения и API окна (`SetLanguageOptions`, `GetSelectedLanguage`, `Populate(state, editorText, hasLocked)`) |
| Goobstation: Ghost Bar (UI-вход) | `Content.Client/UserInterface/Systems/Ghost/Widgets/GhostGui.xaml{,.cs}`, `GhostUIController.cs` | кнопки Ghost Bar в апстримном `GhostGui.xaml` нет, поэтому `GhostBarPressed` было бы мёртвым. Серверный `Content.Server/_Goobstation/Ghostbar/` и окно `GhostBarRulesWindow` живы; события спасены в `Content.Shared/_Goobstation/Ghostbar/GhostBarEvents.cs`. Нужно вернуть кнопку и подписки |
| DeltaV: номер NanoChat на агентской ID-карте | `Content.Client/Access/UI/AgentIDCard*`, `Content.Server/Access/Systems/AgentIDCardSystem.cs` | апстрим выкинул `AgentIDCardBoundUserInterfaceState` и перешёл на состояние компонента + `UpdateUi`. Сам NanoChat (20 файлов) и интеграция с LogProbe сохранены |
| Forge: оптимизации погоды (одна подготовка маски на кадр, кэш маски, интервал пересчёта окклюзии звука) | `Content.Client/Weather/WeatherSystem.cs`, `Content.Client/Overlays/StencilOverlay.cs`, `StencilOverlay.Weather.cs` | апстрим перевёл погоду на статус-эффекты: `Content.Shared/Weather/WeatherComponent.cs` удалён, вместо него `WeatherStatusEffectComponent` + `StatusEffectNew`. Оптимизации нужно переписать под новую архитектуру |

### ВАЖНО для фичи «Пустошные мутации» (ветка feature/wasteland-mutations, не закоммичена)
`RadStormRule` вызывает `SharedWeatherSystem.SetWeather(MapId, WeatherPrototype?, TimeSpan?)`.
После этого апстрима погода стала статус-эффектом, API изменился — ивент радиационной бури
придётся адаптировать перед вливанием ветки.

## Прочее, разрешённое в пользу форка (взята форк-версия файла целиком)
- `Content.Server/Traits/TraitSystem.cs` — система черт с очками и требованиями
- `Content.Server/Thief/Systems/ThiefUndeterminedBackpackSystem.cs`
- `Content.Server/Players/PlayTimeTracking/PlayTimeTrackingSystem.cs` — гейт ролей через CharacterRequirements + спонсоры + DeltaV whitelist
- `Content.Shared/Warps/WarperComponent.cs`, `WarperSystem.cs`
В этих файлах апстрим за 3 года модернизировал API — ошибки компиляции ожидаемы и правятся на сборке.

## Остальное отложенное
- `Content.Server/Chat/Systems/ChatSystem.cs` (форк 1105 строк -> апстрим 298: логика уехала в Shared) и
  `Content.Client/UserInterface/Systems/Chat/ChatUIController.cs` — взят апстрим. Пунктуация чата (`ic.punctuation`)
  теперь есть в апстриме штатно; спонсорский цвет LOOC и скрытие радио от призраков нужно вернуть.
- `Content.Server/Radio/EntitySystems/RadioDeviceSystem.cs` — апстрим оставил 7 строк, логика в
  `Content.Shared/Radio/EntitySystems/SharedRadioDeviceSystem.cs`. TTS-реле голоса через рацию портировать туда.
- `Content.Server/Ghost/Roles/GhostRoleSystem.cs` — `GhostRoleInfo.Requirements` апстрим заменил на
  `RolePrototypes`, поэтому хук `GhostRoleGetRequirementsEvent` (контракты `_NC/Trade`) выброшен.
  Фикс N14 (`UnregisterGhostRole` при takeover) сохранён.
- `Content.Server/Administration/ServerApi.cs` — форк-овые эндпоинты админ-панели перенесены на апстрим-базу
  (`/admin/playtime_trackers`, `/admin/jobs`, `/admin/actions/{role_ban,pardon,add_time}`).
  Форк-овый `ActionBan` убран: у апстрима теперь свой `/admin/actions/ban`.

## Нужна миграция БД
В `Preference` добавлены `SponsorOOCColor`, `SponsorLOOCColor`, `SponsorGhostSkin`;
в `Profile` — `TtsVoice` (переименован из `Voice`) и `BarkVoice`.
Потребуется новая EF-миграция для SQLite и PostgreSQL.
