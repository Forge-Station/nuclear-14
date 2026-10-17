# Что осталось после апстрима b20a6845 -> a8c25ad2

Состояние: мердж закоммичен, конфликтов 0. `Content.Shared` собирается, кроме
файлов, ждущих портов. `Content.Server` и `Content.Client` собрать нельзя, пока
не соберётся `Content.Shared`, поэтому их хвосты пока не измерены.

## Три порта (критический путь)

### 1. Shitmed -> Nubody
Апстрим влил Nubody (#42419): удалены `Content.Shared/Body/Part`, `Body/Organ`,
`SharedBodySystem.{Body,Parts,Organs}`, `BodyPartComponent`. Вместо них граф органов
(`OrganComponent`, `ParentOrganComponent`/`ChildOrganComponent`, `OrganRelationSystem`,
`VisualBodyComponent`). `BodySystem.Compatibility.cs` даёт только один obsolete-хелпер.

| | файлов |
|---|---|
| `Content.Shared/_Shitmed` | 117 |
| `Content.Server/_Shitmed` | 14 |
| `Content.Client/_Shitmed` | 19 |
| ядровые файлы с хуками Shitmed вне `_Shitmed` | 11 |

Ошибок в `Content.Shared` от этого порта: 68.
Дополнительно: `Actions/EntityWorldTargetActionComponent.cs` удалён (старый Actions-API),
его надо воссоздать на модульных действиях апстрима — им пользуются
`Resources/Prototypes/_Shitmed/Body/Actions/organactions.yml`.

### 2. Humanoid
`HumanoidAppearanceComponent` -> `HumanoidProfileComponent`,
`SharedHumanoidAppearanceSystem` -> `HumanoidProfileSystem`, `SkinColor.cs` удалён,
enum `HumanoidSkinColor` заменён на `SkinColorationPrototype` + Strategy.

Затронуто 38 файлов. Ошибок в `Content.Shared`: 2 (`HeightAdjustSystem`,
`DeltaV/Harpy/HarpyVisualsSystem`), остальное — в Server/Client.
Сюда же относится UI лобби, для которого взята апстрим-версия: список фич на
возврат — в `LOST_FORK_FEATURES.md`.

### 3. Эффекты и химия
Апстрим перевёл эффекты на событийную модель: эффект объявляет только данные, а
применяет его `EntityEffectSystem<TComp, TEffect>`; событие несёт лишь
`(Effect, Scale, User)`. Рантайм-контекст реагента (какой реагент сработал, исходный
раствор, метаболизированное количество) больше не передаётся - реагент и количество
стали DataField самого эффекта.

- 30 форк-эффектов ещё на старом API `EntityEffect.Effect(EntityEffectBaseArgs)`
- 33 файла используют `EntityEffectReagentArgs` - им нужен редизайн
- 43 файла переопределяют переименованный `ReagentEffectGuidebookText`

Сделано: `LegacyEntityEffect` (`Content.Shared/_Forge/Compat/`) адаптирует старую форму
к новому конвейеру для эффектов, которым хватает базовых аргументов - на него переведены
7 файлов. Для реагентных он не подходит принципиально.

## Чем это измерять
- `dotnet build Content.Shared/Content.Shared.csproj -c Debug` - текущие ошибки
- `scratchpad/upstream-audit/PORT_CHECKLIST.md` - файлы по каждому удалённому API
- `LOST_FORK_FEATURES.md` - что выброшено и что вернуть портом
- `fork-side-224/` и `fork_changes_224.patch` - форк-версии разобранных вручную файлов
- `LOST_fork_changes.patch` (53 МБ) - форк-изменения в 4725 авторазрешённых файлах

## Долги помимо портов
- Миграция EF: в `Preference` добавлены `SponsorOOCColor/LOOCColor/GhostSkin`,
  в `Profile` - `TtsVoice` (переименован из `Voice`) и `BarkVoice`
- ru-RU FTL админ-вкладок перенаправить на апстримные ключи
  (`admin-ui-atmos-*`, `administration-ui-round-tab-*-round`, `player-tab-player-count`)
- Жажда: `ThirstComponent` ещё пишется, но `ThirstSystem` удалён апстримом в пользу
  `SatiationSystem` - нужна миграция
- `Content.YAMLLinter` по прототипам ещё не прогонялся
