# Порт Shitmed на Nubody: отображение понятий

## Что даёт Nubody (апстрим)

- **Тело** — сущность с `BodyComponent`, внутри плоский контейнер `body_organs`
- **Орган** — сущность с `OrganComponent` (`Body` — обратная ссылка, `Category` —
  `ProtoId<OrganCategoryPrototype>`)
- **Граф** — `ParentOrganComponent.Children` / `ChildOrganComponent.Parent`,
  поддерживает `OrganRelationSystem`
- **Состав тела** — `InitialBodyComponent`: `Organs` (категория -> прототип органа) и
  `Relationships` (категория -> набор дочерних категорий). То есть схема тела
  задаётся данными через категории
- **Отделение** — `DetachableOrganComponent.DetachedBody`: прототип, который спавнится
  при отделении органа. Это ровно механика отрубания конечностей
- **Руки** — `HandOrganComponent` (`HandID` + `Hand`): рука как орган, дающий кисть
- **Визуал** — `VisualOrganComponent`, `VisualBodyComponent`,
  `VisualOrganMarkingsComponent`, `SharedVisualBodySystem.*`
- **Гибы** — `GibbableOrganComponent`
- **События** — `OrganGotInsertedEvent`/`OrganGotRemovedEvent` (на органе),
  `OrganInsertedIntoEvent`/`OrganRemovedFromEvent` (на теле)

Публичное API `BodySystem` узкое: `EnumerateOrgans<T>(body)` и `RelayEvent<T>`.

## Что из Shitmed реально нужно портировать

Замеры по `Content.Shared/_Shitmed`, `Content.Server/_Shitmed`, `Content.Client/_Shitmed`:

| Символ | Ссылок | Решение |
|---|---|---|
| `BodyPartSlot` | 0 | не нужен: контейнер + граф Nubody заменяют слоты |
| `OrganSlot` | 0 | то же |
| `TargetBodyPart` | 135 | жив в `_Shitmed/Targeting/`, трогать не надо |
| `BodyPartComponent` | 46 | вернуть как форк-компонент |
| `BodyPartType` | 69 | вернуть как форк-enum |
| `BodyPartSymmetry` | 30 | вернуть как форк-enum |

Содержимое форк-ового `BodyPartComponent` — почти целиком своё: пороги целостности,
`SeverIntegrity`/`CanSever`, самолечение, `ISurgeryToolComponent`, `Species`,
`BaseLayerId`. От апстрима там были только `Body` и неиспользуемый `ParentSlot`.

## Отображение

| Shitmed | Nubody |
|---|---|
| часть тела | сущность с `OrganComponent` + форк-овый `BodyPartComponent` |
| вложение части в тело | контейнер `body_organs` |
| иерархия частей | `ParentOrganComponent`/`ChildOrganComponent` |
| тип и симметрия части | форк-енумы на `BodyPartComponent`; как ключ слота — `OrganCategoryPrototype` |
| отсечение конечности | `DetachableOrganComponent` |
| рука от конечности | `HandOrganComponent` |
| разметка/внешний вид части | `VisualOrganComponent` + `VisualOrganMarkingsComponent` |
| `SharedBodySystem.*` партиалы | форк-система `ShitmedBodySystem` |

## Ограничение, определяющее имена

У апстрима свой `Content.Shared.Body.OrganComponent`. Форк-овый
`Content.Shared.Body.Organ.OrganComponent` лежит в другом namespace и формально
сосуществует, но Robust берёт имя компонента из имени класса — два
`[RegisterComponent] OrganComponent` дадут одинаковое имя и падение при регистрации.

Поэтому форк-овый организменный компонент переименовывается (`ShitmedOrganComponent`),
и его `type:` в YAML правится соответственно. Имя `BodyPartComponent` свободно —
у апстрима такого нет.

`BodySystem` апстрима — `sealed partial`, и файлы Shitmed лежат в той же сборке, так что
технически в него можно дописаться партиалом. Так делать не стоит: форк-овая логика
хирургии окажется внутри апстримного класса и каждый следующий мердж будет её рвать.
Берём отдельную `ShitmedBodySystem`.

## Порядок работ

1. Вернуть форк-овую таксономию: `BodyPartType`, `BodyPartSymmetry`,
   `BodyPartComponent` (без `ParentSlot`), `ShitmedOrganComponent`
2. Перенацелить партиалы `SharedBodySystem` -> `ShitmedBodySystem`; у потребителей
   снаружи `_Shitmed` развести зависимости между апстримной `BodySystem` и форк-овой
3. Заменить слотовые API на обход графа органов (`EnumerateOrgans`, `Children`/`Parent`)
4. Связать жизненный цикл: `OrganGotInsertedEvent`/`OrganGotRemovedEvent` поддерживают
   `BodyPartComponent.Body`
5. Отсечение перевести на `DetachableOrganComponent`, руки — на `HandOrganComponent`
6. Прототипы: `Resources/Prototypes/_Shitmed/**` — категории органов и схемы тел
   через `InitialBodyComponent`
