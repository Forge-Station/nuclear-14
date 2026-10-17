# Старые копии перенесённого апстримом кода

Апстрим удалил файл у себя (обычно переносил класс в Content.Shared), но у нас
он остался: мердж счёл его форковым, раз у апстрима такого пути больше нет.
Признак дубля точный: тип с тем же именем, что и файл, объявлен ровно в одном
файле, пришедшем от апстрима.

Таких файлов: 65. Все удалены; история и правки форка - ниже.

| удалённый файл | тип | кто теперь владеет | правки форка в истории |
| --- | --- | --- | --- |
| `Content.Client/DeviceNetwork/JammerSystem.cs` | `JammerSystem` | `Content.Shared/Radio/EntitySystems/SharedJammerSystem.cs` | - |
| `Content.Client/Explosion/SmokeOnTriggerSystem.cs` | `SmokeOnTriggerSystem` | `Content.Server/Trigger/Systems/SmokeOnTriggerSystem.cs` | Move grenade components to shared (#22691) |
| `Content.Client/Labels/EntitySystems/LabelSystem.cs` | `LabelSystem` | `Content.Shared/Labels/EntitySystems/LabelSystem.cs` | Add prediction to hand labeler labels (#25869) |
| `Content.Client/MagicMirror/MagicMirrorSystem.cs` | `MagicMirrorSystem` | `Content.Shared/MagicMirror/MagicMirrorSystem.cs` | - |
| `Content.Client/Revolutionary/RevolutionarySystem.cs` | `RevolutionarySystem` | `Content.Shared/Revolutionary/RevolutionarySystem.cs` | Rev Components are no longer leaked + Rev and Zombie icon visibility to ghosts is now controlled by a cvar (#22194); Revolutionaries (#18477) |
| `Content.Server/Administration/Systems/AdminGunSystem.cs` | `AdminGunSystem` | `Content.Shared/Administration/Systems/AdminGunSystem.cs` | Add events for GunComponent values, muzzle flashes and cartridge spread (#24077) |
| `Content.Server/Anomaly/AnomalySynchronizerSystem.cs` | `AnomalySynchronizerSystem` | `Content.Shared/Anomaly/AnomalySynchronizerSystem.cs` | Anomaly Synchronizer + Signallers tweaks (#24461); Anomaly Synchronizer fixes (#21883); Unrevert audio (#21330) |
| `Content.Server/CartridgeLoader/Cartridges/AstroNavCartridgeSystem.cs` | `AstroNavCartridgeSystem` | `Content.Shared/CartridgeLoader/Cartridges/AstroNavCartridgeSystem.cs` | - |
| `Content.Server/CartridgeLoader/Cartridges/LogProbeCartridgeSystem.cs` | `LogProbeCartridgeSystem` | `Content.Shared/CartridgeLoader/Cartridges/LogProbeCartridgeSystem.cs` | Add access logs (IC ones) (#17810) |
| `Content.Server/Chemistry/EntitySystems/ReactionMixerSystem.cs` | `ReactionMixerSystem` | `Content.Shared/Chemistry/Reaction/ReactionMixerSystem.cs` | - |
| `Content.Server/DeviceLinking/Systems/GunSignalControlSystem.cs` | `GunSignalControlSystem` | `Content.Shared/DeviceLinking/Systems/GunSignalControlSystem.cs` | Star Wars 14: Shuttle weapon update (#23644) |
| `Content.Server/DeviceNetwork/Systems/DeviceNetworkJammerSystem.cs` | `DeviceNetworkJammerSystem` | `Content.Shared/DeviceNetwork/Systems/DeviceNetworkJammerSystem.cs` | Mirror: Create DeviceNetworkJammerComponent & System as a general way for entities to act as jammers (#382) |
| `Content.Server/DeviceNetwork/Systems/SingletonDeviceNetServerSystem.cs` | `SingletonDeviceNetServerSystem` | `Content.Shared/DeviceNetwork/Systems/SingletonDeviceNetServerSystem.cs` | Mirror: Create DeviceNetworkJammerComponent & System as a general way for entities to act as jammers (#382); System for single device net server per station functionality (#23946) |
| `Content.Server/Disposal/Tube/Systems/DisposalSignalRouterSystem.cs` | `DisposalSignalRouterSystem` | `Content.Shared/Disposal/SignalRouter/DisposalSignalRouterSystem.cs` | signal router (#20802) |
| `Content.Server/EntityEffects/EffectConditions/JobCondition.cs` | `JobCondition` | `Content.Shared/EntityConditions/Conditions/Mind/JobEntityConditionSystem.cs` | - |
| `Content.Server/EntityEffects/EffectConditions/MobStateCondition.cs` | `MobStateCondition` | `Content.Shared/EntityConditions/Conditions/Body/MobStateEntityConditionSystem.cs` | - |
| `Content.Server/EntityEffects/Effects/AdjustAlert.cs` | `AdjustAlert` | `Content.Shared/EntityEffects/Effects/AdjustAlertEntityEffectSystem.cs` | - |
| `Content.Server/EntityEffects/Effects/AreaReactionEffect.cs` | `AreaReactionEffect` | `Content.Shared/EntityEffects/Effects/Solution/AreaReactionEntityEffect.cs` | - |
| `Content.Server/EntityEffects/Effects/Electrocute.cs` | `Electrocute` | `Content.Shared/EntityEffects/Effects/StatusEffects/ElectrocuteEntityEffectSystem.cs` | - |
| `Content.Server/EntityEffects/Effects/Ignite.cs` | `Ignite` | `Content.Shared/EntityEffects/Effects/Atmos/IgniteEntityEffect.cs` | - |
| `Content.Server/EntityEffects/Effects/MakeSentient.cs` | `MakeSentient` | `Content.Shared/EntityEffects/Effects/MakeSentientEntityEffect.cs` | - |
| `Content.Server/EntityEffects/Effects/ModifyBloodLevel.cs` | `ModifyBloodLevel` | `Content.Shared/EntityEffects/Effects/Body/ModifyBloodLevelEntityEffectSystem.cs` | - |
| `Content.Server/EntityEffects/Effects/ModifyLungGas.cs` | `ModifyLungGas` | `Content.Shared/EntityEffects/Effects/Body/ModifyLungGasEntityEffectSystem.cs` | - |
| `Content.Server/EntityEffects/Effects/Oxygenate.cs` | `Oxygenate` | `Content.Shared/EntityEffects/Effects/Body/OxygenateEntityEffect.cs` | - |
| `Content.Server/EntityEffects/Effects/PlantMetabolism/PlantAdjustHealth.cs` | `PlantAdjustHealth` | `Content.Shared/EntityEffects/Effects/Botany/PlantAttributes/PlantAdjustHealthEntityEffectSystem.cs` | - |
| `Content.Server/EntityEffects/Effects/PlantMetabolism/PlantAdjustMutationLevel.cs` | `PlantAdjustMutationLevel` | `Content.Shared/EntityEffects/Effects/Botany/PlantAttributes/PlantAdjustMutationLevelEntityEffectSystem.cs` | - |
| `Content.Server/EntityEffects/Effects/PlantMetabolism/PlantAdjustMutationMod.cs` | `PlantAdjustMutationMod` | `Content.Shared/EntityEffects/Effects/Botany/PlantAttributes/PlantAdjustMutationModEntityEffectSystem.cs` | - |
| `Content.Server/EntityEffects/Effects/PlantMetabolism/PlantAdjustNutrition.cs` | `PlantAdjustNutrition` | `Content.Shared/EntityEffects/Effects/Botany/PlantAttributes/PlantAdjustNutritionEntityEffectSystem.cs` | - |
| `Content.Server/EntityEffects/Effects/PlantMetabolism/PlantAdjustPests.cs` | `PlantAdjustPests` | `Content.Shared/EntityEffects/Effects/Botany/PlantAttributes/PlantAdjustPestsEntityEffectSystem.cs` | - |
| `Content.Server/EntityEffects/Effects/PlantMetabolism/PlantAdjustToxins.cs` | `PlantAdjustToxins` | `Content.Shared/EntityEffects/Effects/Botany/PlantAttributes/PlantAdjustToxinsEntityEffectSystem.cs` | - |
| `Content.Server/EntityEffects/Effects/PlantMetabolism/PlantAdjustWater.cs` | `PlantAdjustWater` | `Content.Shared/EntityEffects/Effects/Botany/PlantAttributes/PlantAdjustWaterEntityEffectSystem.cs` | - |
| `Content.Server/EntityEffects/Effects/PlantMetabolism/PlantAdjustWeeds.cs` | `PlantAdjustWeeds` | `Content.Shared/EntityEffects/Effects/Botany/PlantAttributes/PlantAdjustWeedsEntityEffectSystem.cs` | - |
| `Content.Server/EntityEffects/Effects/PlantMetabolism/PlantAffectGrowth.cs` | `PlantAffectGrowth` | `Content.Shared/EntityEffects/Effects/Botany/PlantAttributes/PlantAffectGrowthEntityEffectSystem.cs` | - |
| `Content.Server/EntityEffects/Effects/PlantMetabolism/PlantCryoxadone.cs` | `PlantCryoxadone` | `Content.Shared/EntityEffects/Effects/Botany/PlantAttributes/PlantCryoxadoneEntityEffectSystem.cs` | - |
| `Content.Server/EntityEffects/Effects/PlantMetabolism/PlantDiethylamine.cs` | `PlantDiethylamine` | `Content.Shared/EntityEffects/Effects/Botany/PlantAttributes/PlantDiethylamineEntityEffectSystem.cs` | - |
| `Content.Server/EntityEffects/Effects/PlantMetabolism/RobustHarvest.cs` | `RobustHarvest` | `Content.Shared/EntityEffects/Effects/Botany/PlantAttributes/RobustHarvestEntityEffectSystem.cs` | - |
| `Content.Server/EntityEffects/Effects/Polymorph.cs` | `Polymorph` | `Content.Shared/EntityEffects/Effects/PolymorphEntityEffect.cs` | Вирус ВРЭ (#209) |
| `Content.Server/EntityEffects/Effects/ReduceRotting.cs` | `ReduceRotting` | `Content.Shared/EntityEffects/Effects/Body/ReduceRottingEntityEffectSystem.cs` | - |
| `Content.Server/EntityEffects/Effects/StatusEffects/GenericStatusEffect.cs` | `GenericStatusEffect` | `Content.Shared/EntityEffects/Effects/StatusEffects/GenericStatusEffectEntityEffectSystem.cs` | - |
| `Content.Server/EntityEffects/Effects/StatusEffects/Jitter.cs` | `Jitter` | `Content.Shared/EntityEffects/Effects/StatusEffects/JitterEntityEffectSystem.cs` | - |
| `Content.Server/Explosion/EntitySystems/SmokeOnTriggerSystem.cs` | `SmokeOnTriggerSystem` | `Content.Server/Trigger/Systems/SmokeOnTriggerSystem.cs` | Mirror: Obsolete `Logger` cleanup for `EntitySystem`s (#132); Random spontaneous cleanup PR (#25131); Fix clientside storage Whitelists (#24063) |
| `Content.Server/Flash/DamagedByFlashingSystem.cs` | `DamagedByFlashingSystem` | `Content.Shared/Flash/DamagedByFlashingSystem.cs` | Shadow anomaly returns (#24629) |
| `Content.Server/GameTicking/Rules/RevolutionaryRuleSystem.cs` | `RevolutionaryRuleSystem` | `Content.Shared/GameTicking/Rules/RevolutionaryRuleSystem.cs` | Refactor antag rule code (#23445); Obsolete refactor - ConnectedClient to Channel (#24409); better deconversion (#23315) |
| `Content.Server/GameTicking/Rules/RuleGridsSystem.cs` | `RuleGridsSystem` | `Content.Shared/GameTicking/RuleGridsSystem.cs` | - |
| `Content.Server/GameTicking/Rules/ThiefRuleSystem.cs` | `ThiefRuleSystem` | `Content.Shared/GameTicking/Rules/ThiefRuleSystem.cs` | make thief a subgamemode (#25740); Refactor antag rule code (#23445); Ensure correct number of thieves are selected (#23270) |
| `Content.Server/IgnitionSource/IgniteOnTriggerSystem.cs` | `IgniteOnTriggerSystem` | `Content.Server/Trigger/Systems/IgniteOnTriggerSystem.cs` | UseDelay + ItemCooldown merge (#22502); Unrevert audio (#21330); ignition source refactor (#21044) |
| `Content.Server/Mindshield/MindShieldSystem.cs` | `MindShieldSystem` | `Content.Shared/Mindshield/MindShieldSystem.cs` | Fix not removing RevolutionaryRoleComponent from minds on mindshield application (#20832); Revolutionaries (#18477) |
| `Content.Server/Nutrition/EntitySystems/FoodSequenceSystem.cs` | `FoodSequenceSystem` | `Content.Shared/Nutrition/EntitySystems/FoodSequenceSystem.cs` | - |
| `Content.Server/Revolutionary/RevolutionarySystem.cs` | `RevolutionarySystem` | `Content.Shared/Revolutionary/RevolutionarySystem.cs` | - |
| `Content.Server/Spawners/Components/ISpawnPoint.cs` | `ISpawnPoint` | `Content.Shared/Spawners/Components/ISpawnPoint.cs` | Added ContainerSpawnPoint check for integration test (#25446) |
| `Content.Server/Spawners/EntitySystems/ContainerSpawnPointSystem.cs` | `ContainerSpawnPointSystem` | `Content.Shared/Spawners/EntitySystems/ContainerSpawnPointSystem.cs` | Client customization late-join spawner priority for arrivals/cryostorage (#24586); Add prisoner latejoin cryopod (#751); Fix prisoners spawning at latejoin cryopods (#743) |
| `Content.Server/Speech/EmotesMenuSystem.cs` | `EmotesMenuSystem` | `Content.Shared/Speech/EntitySystems/EmotesMenuSystem.cs` | - |
| `Content.Server/Speech/EntitySystems/BarkAccentSystem.cs` | `BarkAccentSystem` | `Content.Shared/Speech/EntitySystems/BarkAccentSystem.cs` | Bread dog (#22548) |
| `Content.Server/Speech/EntitySystems/BlockListeningSystem.cs` | `BlockListeningSystem` | `Content.Shared/Speech/EntitySystems/BlockListeningSystem.cs` | Hackable intercoms (#23984) |
| `Content.Server/Speech/EntitySystems/FrenchAccentSystem.cs` | `FrenchAccentSystem` | `Content.Shared/Speech/EntitySystems/FrenchAccentSystem.cs` | Add French accent beret (#21430) |
| `Content.Server/Speech/EntitySystems/FrontalLispSystem.cs` | `FrontalLispSystem` | `Content.Shared/Speech/EntitySystems/FrontalLispSystem.cs` | Fixed lisp bug with capital and lowercase matching and other sounds matched for clarity (#20370); Added lisp as a character trait (#20350) |
| `Content.Server/Speech/EntitySystems/MothAccentSystem.cs` | `MothAccentSystem` | `Content.Shared/Speech/EntitySystems/MothAccentSystem.cs` | for realsies fix moth accent (#22588); *chitters. (#22563); Moth accent (#21910) |
| `Content.Server/Speech/EntitySystems/ParrotAccentSystem.cs` | `ParrotAccentSystem` | `Content.Shared/Speech/EntitySystems/ParrotAccentSystem.cs` | Mirror: Make parrots talk like parrots (#381) |
| `Content.Server/Speech/EntitySystems/SouthernAccentSystem.cs` | `SouthernAccentSystem` | `Content.Shared/Speech/EntitySystems/SouthernAccentSystem.cs` | - |
| `Content.Server/Speech/EntitySystems/UnblockableSpeechSystem.cs` | `UnblockableSpeechSystem` | `Content.Shared/Speech/EntitySystems/UnblockableSpeechSystem.cs` | Borgs can now speak while in Crit (#21802) |
| `Content.Server/Speech/EntitySystems/VoiceOverrideSystem.cs` | `VoiceOverrideSystem` | `Content.Shared/Speech/EntitySystems/VoiceOverrideSystem.cs` | - |
| `Content.Server/Wagging/WaggingSystem.cs` | `WaggingSystem` | `Content.Shared/Wagging/WaggingSystem.cs` | Stop wagging tails on crit (#25323); Remove tail wag emote text (#25054); Tail wagging (#19573) |
| `Content.Shared/Chat/SharedSuicideSystem.cs` | `SharedSuicideSystem` | `Content.Shared/Suicide/SharedSuicideSystem.cs` | - |
| `Content.Shared/Gibbing/Systems/GibbingSystem.cs` | `GibbingSystem` | `Content.Shared/Gibbing/GibbingSystem.cs` | Random spontaneous cleanup PR (#25131); Gibbing contents hotfix (#25114); Gibbing refactor (Per-part gibbing and giblet throwing!) (#24989) |
| `Content.Shared/SharedLightCycleSystem.cs` | `SharedLightCycleSystem` | `Content.Shared/Light/EntitySystems/SharedLightCycleSystem.cs` | - |

## Как перепроверить

```bash
python -I scratchpad/tools/upstream_deleted.py > updel.txt
python -I scratchpad/tools/stale_moved.py updel.txt
```
