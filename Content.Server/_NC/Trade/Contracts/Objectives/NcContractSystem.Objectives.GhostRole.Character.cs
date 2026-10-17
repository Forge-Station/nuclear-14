using Content.Server.Ghost.Roles;
using Content.Shared._NC.Trade;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Roles.Components;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem : EntitySystem
{
    private void ApplyContractGhostRoleCharacter(EntityUid mob, ContractObjectiveConfigData config)
    {
        if (!string.IsNullOrWhiteSpace(config.GhostRoleCharacterName))
            _contractMeta.SetEntityName(mob, config.GhostRoleCharacterName);

        if (!TryComp(mob, out HumanoidProfileComponent? humanoid))
            return;

        var dirty = false;

        if (config.GhostRoleCharacterSex is { } sex)
        {
            humanoid.Sex = sex;
            dirty = true;
        }

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

        // Forge-Change: цвет кожи и причёски у апстрима больше не поля гуманоида - они
        // стали метками на органах Nubody (SharedVisualBodySystem). Пока контракт их не
        // задаёт; см. долг по переносу прототипов тел.

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
        perks.PassiveHealing = new();
        perks.PassiveHealingInterval = 1f;
        perks.NextPassiveHealing = TimeSpan.Zero;
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

            var hasPassiveHealing = !perks.PassiveHealing.Empty;
            if (!perk.PassiveHealing.Empty)
            {
                perks.PassiveHealing += perk.PassiveHealing;
                perks.PassiveHealingInterval = hasPassiveHealing
                    ? Math.Min(perks.PassiveHealingInterval, perk.PassiveHealingInterval)
                    : perk.PassiveHealingInterval;
            }

            AddUnique(perks.WeaponPrototypes, perk.WeaponPrototypes);
            AddUnique(perks.ArmorItemPrototypes, perk.ArmorItemPrototypes);
            AddFlatReductions(perks.IncomingFlatReductions, perk.IncomingFlatReductions);
        }

        Dirty(mob, perks);
        _contractGhostRoleMovement.RefreshMovementSpeedModifiers(mob);
    }

    private static void AddUnique(List<string> target, IEnumerable<string> source)
    {
        foreach (var value in source)
            if (!string.IsNullOrWhiteSpace(value) && !target.Contains(value))
                target.Add(value);
    }

    private static void AddFlatReductions(
        Dictionary<string, float> target,
        IReadOnlyDictionary<string, float> source
    )
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
            return;

        // Forge-Change: GhostRoleMarkerRoleComponent стал голой меткой на сущности-роли,
        // имя роли теперь берётся из самой сущности.
        if (_contractRoles.MindHasRole<GhostRoleMarkerRoleComponent>(mindId, out var marker))
            _contractMeta.SetEntityName(marker.Value.Owner, Loc.GetString("nc-store-contract-ghost-role-manifest-role"));

        AddGhostRoleBriefing(mindId, contract);
        TryAddGhostRoleSurvivalObjective(key, state, contract, mindId, mind);
    }
}
