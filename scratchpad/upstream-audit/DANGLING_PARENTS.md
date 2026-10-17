# Прототипы без родителя

Нашлось сплошной проверкой `Resources/Prototypes`: прототип ссылается на
`parent`, которого в дереве больше нет. На старте это падение загрузки.
Причина - политика мерджа "Resources берём у апстрима": форковые сущности,
лежавшие внутри апстримных файлов, исчезли, а дети в форковых файлах остались.

Потерянных родителей: 59, затронутых детей: 328.
Из них были в форке до мерджа: 58.

## Телесные прототипы - часть порта на Nubody

Апстрим (#42419) выбросил таксономию `BodyPart`/`Organ` вместе с её прототипами.
Эти родители восстанавливать нельзя: детей надо переносить на органы Nubody
или удалять вместе с ними. Это шаг 6 из NUBODY_PORT_DESIGN.md.

| родитель | где был в форке | детей |
| --- | --- | --- |
| `BaseHead` | Resources/Prototypes/Body/Parts/base.yml | 9 |
| `BaseLeftArm` | Resources/Prototypes/Body/Parts/base.yml | 8 |
| `BaseLeftFoot` | Resources/Prototypes/Body/Parts/base.yml | 8 |
| `BaseLeftHand` | Resources/Prototypes/Body/Parts/base.yml | 8 |
| `BaseLeftLeg` | Resources/Prototypes/Body/Parts/base.yml | 8 |
| `BasePart` | Resources/Prototypes/Body/Parts/base.yml | 8 |
| `BaseRightArm` | Resources/Prototypes/Body/Parts/base.yml | 8 |
| `BaseRightFoot` | Resources/Prototypes/Body/Parts/base.yml | 8 |
| `BaseRightHand` | Resources/Prototypes/Body/Parts/base.yml | 8 |
| `BaseRightLeg` | Resources/Prototypes/Body/Parts/base.yml | 8 |
| `BaseTorso` | Resources/Prototypes/Body/Parts/base.yml | 8 |
| `BaseHumanOrgan` | Resources/Prototypes/Body/Organs/human.yml | 5 |
| `LeftArmHuman` | Resources/Prototypes/Body/Parts/human.yml | 3 |
| `RightArmHuman` | Resources/Prototypes/Body/Parts/human.yml | 3 |
| `PartAnimal` | Resources/Prototypes/Body/Parts/animal.yml | 2 |
| `LeftFootHuman` | Resources/Prototypes/Body/Parts/human.yml | 2 |
| `LeftHandHuman` | Resources/Prototypes/Body/Parts/human.yml | 2 |
| `LeftLegHuman` | Resources/Prototypes/Body/Parts/human.yml | 2 |
| `RightFootHuman` | Resources/Prototypes/Body/Parts/human.yml | 2 |
| `RightHandHuman` | Resources/Prototypes/Body/Parts/human.yml | 2 |
| `RightLegHuman` | Resources/Prototypes/Body/Parts/human.yml | 2 |
| `PartHuman` | Resources/Prototypes/Body/Parts/human.yml | 2 |
| `BaseAnimalOrgan` | Resources/Prototypes/Body/Organs/Animal/animal.yml | 2 |
| `BaseBorgHead` | Resources/Prototypes/Body/Parts/silicon.yml | 1 |
| `BaseBorgArmLeft` | Resources/Prototypes/Body/Parts/silicon.yml | 1 |
| `BaseBorgLegLeft` | Resources/Prototypes/Body/Parts/silicon.yml | 1 |
| `BaseBorgArmRight` | Resources/Prototypes/Body/Parts/silicon.yml | 1 |
| `BaseBorgLegRight` | Resources/Prototypes/Body/Parts/silicon.yml | 1 |
| `BaseBorgTorso` | Resources/Prototypes/Body/Parts/silicon.yml | 1 |
| `BaseAnimalOrganUnGibbable` | Resources/Prototypes/Body/Organs/Animal/animal.yml | 1 |

## Остальное - потери мерджа

Тут родителя надо либо вернуть из версии до мерджа, либо, если апстрим
переименовал аналог, перенаправить детей на апстримное имя. Проверять
по каждому отдельно.

| родитель | был в форке | где был | детей |
| --- | --- | --- | --- |
| `ClothingHeadEnvirohelmCustomBase` | да | Resources/Prototypes/Entities/Clothing/Head/base_clothinghead.yml | 44 |
| `ClothingHeadEnvirohelmBase` | да | Resources/Prototypes/Entities/Clothing/Head/base_clothinghead.yml | 31 |
| `ClothingHandsGlovesEnviroglovesBase` | да | Resources/Prototypes/Entities/Clothing/Hands/base_clothinghands.yml | 23 |
| `MobHumanDummy` | да | Resources/Prototypes/Entities/Mobs/Species/human.yml | 13 |
| `ChangelingFleshClothingBase` | нет | - | 12 |
| `ClothingHeadHardsuitWithLightBase` | да | Resources/Prototypes/Entities/Clothing/Head/base_clothinghead.yml | 11 |
| `BaseMobSpeciesOrganic` | да | Resources/Prototypes/Entities/Mobs/Species/base.yml | 10 |
| `ClothingOuterFoldableBaseOpened` | да | Resources/Prototypes/Entities/Clothing/OuterClothing/base_clothingouter.yml | 10 |
| `BaseMobHuman` | да | Resources/Prototypes/Entities/Mobs/Species/human.yml | 8 |
| `WindoorSecure` | да | Resources/Prototypes/Entities/Structures/Doors/Windoors/windoor.yml | 6 |
| `LockerBaseSecure` | да | Resources/Prototypes/Entities/Structures/Storage/Closets/Lockers/base_structurelockers.yml | 5 |
| `BaseSpeciesDummy` | да | Resources/Prototypes/Entities/Mobs/Species/base.yml | 5 |
| `ClothingOuterStorageToggleableBase` | да | Resources/Prototypes/Entities/Clothing/OuterClothing/base_clothingouter.yml | 2 |
| `ClothingOuterHardsuitBaseHeavy` | да | Resources/Prototypes/Entities/Clothing/OuterClothing/base_clothingouter.yml | 2 |
| `ClothingOuterHardsuitBaseMedium` | да | Resources/Prototypes/Entities/Clothing/OuterClothing/base_clothingouter.yml | 2 |
| `DrinkGlassBase` | да | Resources/Prototypes/Entities/Objects/Consumable/Drinks/drinks.yml | 2 |
| `MobXenoDroneNPC` | да | Resources/Prototypes/Entities/Mobs/NPCs/xeno.yml | 2 |
| `MobXenoPraetorianNPC` | да | Resources/Prototypes/Entities/Mobs/NPCs/xeno.yml | 2 |
| `MobXenoRavagerNPC` | да | Resources/Prototypes/Entities/Mobs/NPCs/xeno.yml | 2 |
| `MobXenoRounyNPC` | да | Resources/Prototypes/Entities/Mobs/NPCs/xeno.yml | 2 |
| `BaseBulletRubber` | да | Resources/Prototypes/Entities/Objects/Weapons/Guns/Projectiles/projectiles.yml | 1 |
| `ClothingNeckMedalBase` | да | Resources/Prototypes/Entities/Clothing/Neck/medals.yml | 1 |
| `MobXenoQueenNPC` | да | Resources/Prototypes/Entities/Mobs/NPCs/xeno.yml | 1 |
| `MobXenoRunnerNPC` | да | Resources/Prototypes/Entities/Mobs/NPCs/xeno.yml | 1 |
| `MobXenoSpitterNPC` | да | Resources/Prototypes/Entities/Mobs/NPCs/xeno.yml | 1 |
| `ClothingBeltAssault` | да | Resources/Prototypes/Entities/Clothing/Belt/belts.yml | 1 |
| `HandheldHealthAnalyzerUnpowered` | да | Resources/Prototypes/Entities/Objects/Specific/Medical/healthanalyzer.yml | 1 |
| `OmnimedTool` | да | Resources/Prototypes/Entities/Objects/Specific/Medical/surgery.yml | 1 |
| `WeaponRifleM90GrenadeLauncher` | да | Resources/Prototypes/Entities/Objects/Weapons/Guns/Rifles/rifles.yml | 1 |

## Как перепроверить

```bash
python -I scratchpad/tools/dangling_parents.py
```

Когда Content.Server и Content.Client начнут собираться, эту проверку
заменит `Content.YAMLLinter`: он видит не только родителей, но и любые
несуществующие ProtoId, и знает про поля компонентов.
