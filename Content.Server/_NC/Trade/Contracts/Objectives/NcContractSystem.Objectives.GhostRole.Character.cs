using Content.Server.Atmos.Rotting;
using Content.Server.Cuffs;
using Content.Server.Ghost.Roles.Components;
using Content.Server.Humanoid;
using Content.Server.Mind.Commands;
using Content.Server.Roles;
using Content.Shared._NC.Trade;
using Content.Shared.Cuffs.Components;
using Content.Shared.Customization.Systems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private void ApplyContractGhostRoleCharacter(EntityUid mob, ContractObjectiveConfigData config)
    {
        if (!string.IsNullOrWhiteSpace(config.GhostRoleCharacterName))
            _contractMeta.SetEntityName(mob, config.GhostRoleCharacterName);

        if (!TryComp(mob, out HumanoidAppearanceComponent? humanoid))
            return;

        var dirty = false;

        if (config.GhostRoleCharacterSex is { } sex)
            _contractGhostRoleHumanoid.SetSex(mob, sex, false, humanoid);

        if (config.GhostRoleCharacterGender is { } gender)
        {
            humanoid.Gender = gender;
            dirty = true;
        }

        if (config.GhostRoleCharacterAge is { } age)
        {
            humanoid.Age = Math.Max(0, age);
            dirty = true;
        }

        if (config.GhostRoleCharacterSkinColor is { } skinColor)
        {
            _contractGhostRoleHumanoid.SetSkinColor(mob, skinColor, false, true, humanoid);
            dirty = true;
        }

        if (!string.IsNullOrWhiteSpace(config.GhostRoleCharacterHair))
        {
            humanoid.MarkingSet.RemoveCategory(MarkingCategories.Hair);
            _contractGhostRoleHumanoid.AddMarking(
                mob,
                config.GhostRoleCharacterHair,
                config.GhostRoleCharacterHairColor,
                false,
                true,
                humanoid);
            dirty = true;
        }

        if (dirty)
            Dirty(mob, humanoid);
    }

    private void ApplyContractGhostRolePerks(EntityUid mob, ContractObjectiveConfigData config)
    {
        if (config.GhostRolePerks.Count == 0)
            return;

        var perks = EnsureComp<NcContractGhostRolePerksComponent>(mob);
        perks.PerkIds.Clear();
        perks.WalkSpeedMultiplier = 1f;
        perks.SprintSpeedMultiplier = 1f;
        perks.IncomingDamageMultiplier = 1f;
        perks.MeleeDamageMultiplier = 1f;
        perks.ProjectileDamageMultiplier = 1f;
        perks.WeaponPrototypes.Clear();
        perks.ArmorItemPrototypes.Clear();
        perks.ArmorIncomingDamageMultiplier = 1f;
        perks.IncomingFlatReductions.Clear();

        foreach (var perkId in config.GhostRolePerks)
        {
            if (!_prototypes.TryIndex<NcGhostRolePerkPrototype>(perkId, out var perk))
                continue;

            perks.PerkIds.Add(perk.ID);
            perks.WalkSpeedMultiplier *= perk.WalkSpeedMultiplier;
            perks.SprintSpeedMultiplier *= perk.SprintSpeedMultiplier;
            perks.IncomingDamageMultiplier *= perk.IncomingDamageMultiplier;
            perks.MeleeDamageMultiplier *= perk.MeleeDamageMultiplier;
            perks.ProjectileDamageMultiplier *= perk.ProjectileDamageMultiplier;
            perks.ArmorIncomingDamageMultiplier *= perk.ArmorIncomingDamageMultiplier;
            AddUnique(perks.WeaponPrototypes, perk.WeaponPrototypes);
            AddUnique(perks.ArmorItemPrototypes, perk.ArmorItemPrototypes);
            AddFlatReductions(perks.IncomingFlatReductions, perk.IncomingFlatReductions);
        }

        Dirty(mob, perks);
    }

    private static void AddUnique(List<string> target, IEnumerable<string> source)
    {
        foreach (var value in source)
        {
            if (!string.IsNullOrWhiteSpace(value) && !target.Contains(value))
                target.Add(value);
        }
    }

    private static void AddFlatReductions(
        Dictionary<string, float> target,
        IReadOnlyDictionary<string, float> source)
    {
        foreach (var (damageType, reduction) in source)
        {
            if (string.IsNullOrWhiteSpace(damageType) || reduction <= 0f)
                continue;

            target[damageType] = target.TryGetValue(damageType, out var existing)
                ? existing + reduction
                : reduction;
        }
    }

    private void TryAttachGhostRoleCharacterInfo(EntityUid mob)
    {
        if (!_objectiveRuntime.ByTarget.TryGetValue(mob, out var key) ||
            !_objectiveRuntime.ByContract.TryGetValue(key, out var state) ||
            !TryGetObjectiveContract(key, out _, out var contract) ||
            !_contractMind.TryGetMind(mob, out var mindId, out var mind))
        {
            return;
        }

        AddGhostRoleBriefing(mindId, contract);
        TryAddGhostRoleSurvivalObjective(key, state, contract, mindId, mind);
    }
}
