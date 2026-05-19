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
    [Dependency] private readonly RottingSystem _contractGhostRoleRotting = default!;
    [Dependency] private readonly CuffableSystem _contractGhostRoleCuffs = default!;
    [Dependency] private readonly HumanoidAppearanceSystem _contractGhostRoleHumanoid = default!;

    private bool TryInitializeGhostRoleObjective(
        EntityUid store,
        EntityUid user,
        string contractId,
        ContractServerData contract
    )
    {
        if (!TryResolveGhostRolePrototype(contractId, contract, out var ghostRoleProtoId))
            return false;

        var config = contract.Config;
        ResetObjectiveState(contract);
        config.GhostRolePrototype = ghostRoleProtoId;
        if (!TryResolveGhostRoleSpawnCoordinates(store, contractId, config, out var spawnCoords))
            return false;

        if (!TrySpawnGhostRoleSpawner(contractId, spawnCoords, out var spawner))
            return false;

        ConfigureGhostRoleSpawner(spawner, contract, ghostRoleProtoId);
        RegisterGhostRoleObjectiveState((store, contractId), spawner, contract);
        return true;
    }

    private bool TryResolveGhostRolePrototype(
        string contractId,
        ContractServerData contract,
        out string ghostRoleProtoId
    )
    {
        ghostRoleProtoId = ResolveTrackedObjectivePrototypeId(
            contract.Config.GhostRolePrototype,
            contract.TargetItem);
        if (!string.IsNullOrWhiteSpace(ghostRoleProtoId) && _prototypes.HasIndex<EntityPrototype>(ghostRoleProtoId))
            return true;

        Sawmill.Warning(
            $"[Contracts] Ghost role init failed for '{contractId}': ghost role prototype '{ghostRoleProtoId}' is missing.");
        return false;
    }

    private bool TryResolveGhostRoleSpawnCoordinates(
        EntityUid store,
        string contractId,
        ContractObjectiveConfigData config,
        out EntityCoordinates spawnCoords
    )
    {
        if (TryResolveObjectiveSpawnCoordinates(store, config, out spawnCoords))
            return true;

        Sawmill.Warning($"[Contracts] Ghost role init failed for '{contractId}': cannot resolve spawn coordinates.");
        return false;
    }

    private bool TrySpawnGhostRoleSpawner(
        string contractId,
        EntityCoordinates spawnCoords,
        out EntityUid spawner
    )
    {
        try
        {
            spawner = Spawn(null, spawnCoords);
            return true;
        }
        catch (Exception e)
        {
            Sawmill.Error(
                $"[Contracts] Ghost role init failed for '{contractId}': runtime spawner creation threw: {e}");
            spawner = EntityUid.Invalid;
            return false;
        }
    }

    private void ConfigureGhostRoleSpawner(EntityUid spawner, ContractServerData contract, string ghostRoleProtoId)
    {
        var config = contract.Config;
        var ghostRole = EnsureComp<GhostRoleComponent>(spawner);
        ghostRole.RoleName = ResolveContractGhostRoleName(config, contract);
        ghostRole.RoleDescription = ResolveContractGhostRoleDescription(config, contract);
        ghostRole.RoleRules = ResolveContractGhostRoleRules(config);

        var spawnerComp = EnsureComp<NcContractGhostRoleSpawnerComponent>(spawner);
        spawnerComp.TargetPrototype = ghostRoleProtoId;
        spawnerComp.Requirements = config.GhostRoleRequirements.Count > 0
            ? new(config.GhostRoleRequirements)
            : new List<CharacterRequirement>();
    }

    private static string
        ResolveContractGhostRoleName(ContractObjectiveConfigData config, ContractServerData contract) =>
        string.IsNullOrWhiteSpace(config.GhostRoleName)
            ? contract.Name
            : config.GhostRoleName;

    private static string ResolveContractGhostRoleDescription(
        ContractObjectiveConfigData config,
        ContractServerData contract
    ) =>
        string.IsNullOrWhiteSpace(config.GhostRoleDescription)
            ? contract.Description
            : config.GhostRoleDescription;

    private static string ResolveContractGhostRoleRules(ContractObjectiveConfigData config) =>
        string.IsNullOrWhiteSpace(config.GhostRoleRules)
            ? "ghost-role-component-default-rules"
            : config.GhostRoleRules;

    private void RegisterGhostRoleObjectiveState(
        (EntityUid Store, string ContractId) key,
        EntityUid spawner,
        ContractServerData contract
    )
    {
        var config = contract.Config;
        var runtime = contract.Runtime;
        var state = GetOrCreateObjectiveRuntimeState(key);
        state.TargetEntity = spawner;
        state.GhostRoleTaken = false;
        state.GhostRoleAcceptDeadline = config.AcceptTimeoutSeconds > 0
            ? _timing.CurTime + TimeSpan.FromSeconds(config.AcceptTimeoutSeconds)
            : null;
        RegisterGhostRoleRoundEndRecord(key, contract, state);
        _objectiveRuntime.ActiveGhostRoleObjectives.Add(key);
        _objectiveRuntime.ByTarget[spawner] = key;

        runtime.GhostRolePendingAcceptance = state.GhostRoleAcceptDeadline != null;
        runtime.AcceptTimeoutRemainingSeconds = runtime.GhostRolePendingAcceptance
            ? Math.Max(0, config.AcceptTimeoutSeconds)
            : 0;
        runtime.GhostRoleSurvivalRemainingSeconds = 0;
        runtime.StatusHint = runtime.GhostRolePendingAcceptance
            ? Loc.GetString("nc-store-contract-ghost-role-hint-waiting")
            : string.Empty;
    }

    private void OnContractGhostRoleTakeover(
        EntityUid uid,
        NcContractGhostRoleSpawnerComponent comp,
        ref TakeGhostRoleEvent args
    )
    {
        if (!TryComp(uid, out GhostRoleComponent? ghostRole) ||
            comp.Claimed ||
            !CanTakeContractGhostRole(args.Player, uid, comp, ghostRole))
        {
            args.TookRole = false;
            return;
        }

        if (string.IsNullOrWhiteSpace(comp.TargetPrototype) ||
            !_prototypes.HasIndex<EntityPrototype>(comp.TargetPrototype))
        {
            Sawmill.Warning(
                $"[Contracts] Ghost role take failed for {ToPrettyString(uid)}: invalid prototype '{comp.TargetPrototype}'.");
            args.TookRole = false;
            return;
        }

        var mob = Spawn(comp.TargetPrototype, Transform(uid).Coordinates);
        _xform.AttachToGridOrMap(mob);

        if (!TryActivateGhostRoleContractTarget(uid, mob))
        {
            QueueDel(mob);
            args.TookRole = false;
            return;
        }

        if (ghostRole.MakeSentient)
            MakeSentientCommand.MakeSentient(mob, EntityManager, ghostRole.AllowMovement, ghostRole.AllowSpeech);

        EnsureComp<MindContainerComponent>(mob);
        _ghostRoles.GhostRoleInternalCreateMindAndTransfer(args.Player, uid, mob, ghostRole);
        TryAttachGhostRoleCharacterInfo(mob);
        if (_objectiveRuntime.ByTarget.TryGetValue(mob, out var activeKey) &&
            _objectiveRuntime.ByContract.TryGetValue(activeKey, out var state) &&
            TryGetObjectiveContract(activeKey, out _, out var activeContract))
        {
            ApplyContractGhostRoleCharacter(mob, activeContract.Config);
            ApplyContractGhostRolePerks(mob, activeContract.Config);
            MarkGhostRoleRoundEndTaken(state, activeContract, mob, args.Player.Name);
        }

        comp.Claimed = true;
        _ghostRoles.UnregisterGhostRole((uid, ghostRole));
        QueueDel(uid);

        args.TookRole = true;
    }

    private bool TryActivateGhostRoleContractTarget(EntityUid spawner, EntityUid target)
    {
        if (!_objectiveRuntime.ByTarget.TryGetValue(spawner, out var key))
            return false;

        if (!_objectiveRuntime.ByContract.TryGetValue(key, out var state))
            return false;

        if (!TryGetObjectiveContract(key, out _, out var contract) ||
            !contract.Taken ||
            contract.Completed ||
            !contract.IsGhostRoleObjective ||
            contract.Runtime.Failed)
            return false;

        EnsureObjectiveRuntimeDefaults(contract);

        if (!state.GhostRoleTaken && state.GhostRoleAcceptDeadline is { } deadline && _timing.CurTime >= deadline)
        {
            FailExpiredGhostRoleObjective(key);
            return false;
        }

        _objectiveRuntime.ByTarget.Remove(spawner);
        state.TargetEntity = target;
        state.GhostRoleTaken = true;
        state.GhostRoleAcceptDeadline = null;
        if (contract.Config.GhostRoleSurvivalDurationSeconds > 0)
        {
            state.GhostRoleSurvivalStart = _timing.CurTime;
            state.GhostRoleSurvivalDeadline =
                _timing.CurTime + TimeSpan.FromSeconds(contract.Config.GhostRoleSurvivalDurationSeconds);
            state.GhostRoleSurvivalSucceeded = false;
        }

        var runtime = contract.Runtime;
        runtime.GhostRolePendingAcceptance = false;
        runtime.AcceptTimeoutRemainingSeconds = 0;
        SyncGhostRoleSurvivalRemaining(state, runtime);
        runtime.StatusHint = Loc.GetString("nc-store-contract-ghost-role-hint-deliver");
        _objectiveRuntime.ByTarget[target] = key;

        RetargetObjectivePinpointers(key, state, target);
        return true;
    }

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

    private void AddGhostRoleBriefing(EntityUid mindId, ContractServerData contract)
    {
        var briefing = BuildGhostRoleBriefing(contract);
        if (string.IsNullOrWhiteSpace(briefing))
            return;

        var briefingComp = EnsureComp<RoleBriefingComponent>(mindId);
        if (string.IsNullOrWhiteSpace(briefingComp.Briefing))
        {
            briefingComp.Briefing = briefing;
            return;
        }

        if (!briefingComp.Briefing.Contains(briefing, StringComparison.Ordinal))
            briefingComp.Briefing += "\n" + briefing;
    }

    private string BuildGhostRoleBriefing(ContractServerData contract)
    {
        var config = contract.Config;
        var description = string.IsNullOrWhiteSpace(config.GhostRoleDescription)
            ? contract.Description
            : ResolveGhostRoleLocaleText(config.GhostRoleDescription);
        var rules = string.IsNullOrWhiteSpace(config.GhostRoleRules)
            ? string.Empty
            : ResolveGhostRoleLocaleText(config.GhostRoleRules);
        if (!string.IsNullOrWhiteSpace(rules))
            description = string.IsNullOrWhiteSpace(description)
                ? rules
                : $"{description}\n{rules}";

        var survival = config.GhostRoleSurvivalDurationSeconds > 0
            ? ResolveGhostRoleSurvivalBriefing(config)
            : string.Empty;

        if (string.IsNullOrWhiteSpace(description))
            return survival;

        if (string.IsNullOrWhiteSpace(survival))
            return Loc.GetString("nc-store-contract-ghost-role-character-briefing", ("contract", contract.Name), ("description", description));

        return Loc.GetString(
            "nc-store-contract-ghost-role-character-briefing-survival",
            ("contract", contract.Name),
            ("description", description),
            ("survival", survival));
    }

    private string ResolveGhostRoleSurvivalBriefing(ContractObjectiveConfigData config)
    {
        if (!string.IsNullOrWhiteSpace(config.GhostRoleSurvivalBriefing))
            return ResolveGhostRoleLocaleText(config.GhostRoleSurvivalBriefing);

        return Loc.GetString(
            "nc-store-contract-ghost-role-survival-briefing",
            ("time", FormatGhostRoleDurationText(config.GhostRoleSurvivalDurationSeconds)));
    }

    private void TryAddGhostRoleSurvivalObjective(
        (EntityUid Store, string ContractId) key,
        ObjectiveRuntimeState state,
        ContractServerData contract,
        EntityUid mindId,
        MindComponent mind)
    {
        var config = contract.Config;
        if (config.GhostRoleSurvivalDurationSeconds <= 0 ||
            state.GhostRoleSurvivalObjective is { } existing && existing != EntityUid.Invalid)
        {
            return;
        }

        var start = state.GhostRoleSurvivalStart ?? _timing.CurTime;
        var deadline = state.GhostRoleSurvivalDeadline ??
                       start + TimeSpan.FromSeconds(config.GhostRoleSurvivalDurationSeconds);

        var objective = Spawn("NcContractGhostRoleSurvivalObjective", MapCoordinates.Nullspace);
        _contractMeta.SetEntityName(objective, ResolveGhostRoleSurvivalObjectiveTitle(contract));
        _contractMeta.SetEntityDescription(objective, ResolveGhostRoleSurvivalObjectiveDescription(contract));

        var survival = EnsureComp<NcContractGhostRoleSurvivalObjectiveComponent>(objective);
        survival.Store = key.Store;
        survival.ContractId = key.ContractId;
        survival.StartedAt = start;
        survival.Deadline = deadline;
        survival.Finished = false;
        survival.Succeeded = false;

        _contractMind.AddObjective(mindId, mind, objective);
        state.GhostRoleSurvivalMind = mindId;
        state.GhostRoleSurvivalObjective = objective;
    }

    private string ResolveGhostRoleSurvivalObjectiveTitle(ContractServerData contract)
    {
        if (!string.IsNullOrWhiteSpace(contract.Config.GhostRoleSurvivalObjectiveTitle))
            return ResolveGhostRoleLocaleText(contract.Config.GhostRoleSurvivalObjectiveTitle);

        return Loc.GetString(
            "nc-store-contract-ghost-role-survival-objective-title",
            ("contract", contract.Name));
    }

    private string ResolveGhostRoleSurvivalObjectiveDescription(ContractServerData contract)
    {
        if (!string.IsNullOrWhiteSpace(contract.Config.GhostRoleSurvivalObjectiveDescription))
            return ResolveGhostRoleLocaleText(contract.Config.GhostRoleSurvivalObjectiveDescription);

        return Loc.GetString(
            "nc-store-contract-ghost-role-survival-objective-description",
            ("time", FormatGhostRoleDurationText(contract.Config.GhostRoleSurvivalDurationSeconds)));
    }

    private string ResolveGhostRoleLocaleText(string text)
    {
        return Loc.TryGetString(text, out var localized)
            ? localized
            : text;
    }

    private string FormatGhostRoleDurationText(int totalSeconds)
    {
        var seconds = Math.Max(1, totalSeconds);
        var span = TimeSpan.FromSeconds(seconds);
        var parts = new List<string>(2);

        if (span.Hours + (span.Days * 24) > 0)
        {
            var hours = span.Hours + (span.Days * 24);
            parts.Add(Loc.GetString("nc-store-contract-duration-hours", ("count", hours)));
        }

        if (span.Minutes > 0 && parts.Count < 2)
            parts.Add(Loc.GetString("nc-store-contract-duration-minutes", ("count", span.Minutes)));

        if (parts.Count == 0)
            parts.Add(Loc.GetString("nc-store-contract-duration-seconds", ("count", span.Seconds)));

        return string.Join(" ", parts);
    }

    private bool TryCompleteGhostRoleSurvivalObjective(
        (EntityUid Store, string ContractId) key,
        ObjectiveRuntimeState state,
        NcStoreComponent comp,
        ContractServerData contract)
    {
        if (contract.Config.GhostRoleSurvivalDurationSeconds <= 0 ||
            state.GhostRoleSurvivalSucceeded ||
            state.GhostRoleSurvivalDeadline is not { } deadline ||
            _timing.CurTime < deadline ||
            state.TargetEntity is not { } target ||
            target == EntityUid.Invalid ||
            TerminatingOrDeleted(target) ||
            _contractGhostRoleRotting.IsRotten(target) ||
            !IsGhostRoleTargetAlive(target) ||
            IsGhostRoleCompletionSatisfied(key.Store, target, contract))
        {
            return false;
        }

        state.GhostRoleSurvivalSucceeded = true;
        MarkGhostRoleRoundEndOutcome(
            state,
            GhostRoleRoundEndOutcome.RoleSurvived,
            Loc.GetString("nc-store-contract-ghost-role-survival-succeeded"));
        if (state.GhostRoleSurvivalObjective is { } objective &&
            !TerminatingOrDeleted(objective) &&
            TryComp(objective, out NcContractGhostRoleSurvivalObjectiveComponent? survival))
        {
            survival.Finished = true;
            survival.Succeeded = true;
        }

        FinalizeObjectiveTerminalOutcome(
            key,
            comp,
            contract,
            Loc.GetString("nc-store-contract-ghost-role-survival-succeeded"),
            ContractObjectiveOutcome.RoleSurvived,
            deleteTrackedEntities: false);
        return true;
    }

    // Ghost role objective runtime.
    private void UpdateGhostRoleObjectiveTimeouts()
    {
        if (_objectiveRuntime.ActiveGhostRoleObjectives.Count == 0)
            return;

        _objectiveRuntime.KeysScratch.Clear();
        foreach (var key in _objectiveRuntime.ActiveGhostRoleObjectives)
            _objectiveRuntime.KeysScratch.Add(key);

        for (var i = 0; i < _objectiveRuntime.KeysScratch.Count; i++)
        {
            var key = _objectiveRuntime.KeysScratch[i];
            if (!_objectiveRuntime.ByContract.TryGetValue(key, out var state))
            {
                _objectiveRuntime.ActiveGhostRoleObjectives.Remove(key);
                continue;
            }

            if (state.GhostRoleTaken)
            {
                if (TryGetObjectiveContract(key, out var comp, out var contract) &&
                    TryFailGhostRoleTargetIfInvalidOrRotten(key, state, comp, contract))
                {
                    continue;
                }

                if (TryCompleteGhostRoleSurvivalObjective(key, state, comp, contract))
                    continue;

                TryRetargetGhostRolePinpointersForOwners(key, state);
                continue;
            }

            if (state.GhostRoleAcceptDeadline is not { } deadline)
                continue;

            if (_timing.CurTime >= deadline)
                FailExpiredGhostRoleObjective(key);
        }

        _objectiveRuntime.KeysScratch.Clear();
    }

    private bool TryRetargetGhostRolePinpointersForOwners(
        (EntityUid Store, string ContractId) key,
        ObjectiveRuntimeState state)
    {
        if (state.PinpointerEntities.Count == 0)
            return false;

        if (!TryGetObjectiveContract(key, out _, out var contract) ||
            !contract.Taken ||
            contract.Runtime.Failed ||
            !contract.IsGhostRoleObjective ||
            !state.GhostRoleTaken)
        {
            return false;
        }

        PruneInvalidPinpointers(key, state);
        if (state.PinpointerEntities.Count == 0)
            return true;

        foreach (var pinpointer in state.PinpointerEntities)
        {
            if (TerminatingOrDeleted(pinpointer))
                continue;

            if (!_pinpointerService.TryGetOwner(_objectiveRuntime, pinpointer, out var owner) ||
                !TryResolveGhostRolePinpointerTargetForUser(key.Store, owner, contract, state, out var target) ||
                target == EntityUid.Invalid ||
                TerminatingOrDeleted(target))
            {
                continue;
            }

            _pinpointer.SetTarget(pinpointer, target);
            _pinpointer.SetActive(pinpointer, true);
        }

        return true;
    }

    private bool TryResolveGhostRolePinpointerTargetForUser(
        EntityUid store,
        EntityUid user,
        ContractServerData contract,
        ObjectiveRuntimeState state,
        out EntityUid target)
    {
        target = EntityUid.Invalid;
        if (!contract.IsGhostRoleObjective || !state.GhostRoleTaken)
            return false;

        if (state.TargetEntity is not { } tracked ||
            tracked == EntityUid.Invalid ||
            TerminatingOrDeleted(tracked))
        {
            return false;
        }

        if (IsGhostRoleTargetReadyForClaim(store, tracked, contract) ||
            IsGhostRoleTargetCarriedByUser(tracked, user))
        {
            target = store;
            return true;
        }

        target = tracked;
        return true;
    }

    private void FailExpiredGhostRoleObjective((EntityUid Store, string ContractId) key)
    {
        if (!_objectiveRuntime.ByContract.TryGetValue(key, out var state) ||
            state.GhostRoleTaken ||
            state.GhostRoleAcceptDeadline is not { } deadline ||
            _timing.CurTime < deadline)
            return;

        if (!TryGetObjectiveContract(key, out var comp, out var contract))
        {
            CleanupObjectiveRuntime(key.Store, key.ContractId, true);
            return;
        }

        if (!contract.Taken || !contract.IsGhostRoleObjective || contract.Completed)
            return;

        MarkGhostRoleRoundEndOutcome(
            state,
            GhostRoleRoundEndOutcome.NotAccepted,
            Loc.GetString("nc-store-contract-ghost-role-timeout"));
        FinalizeObjectiveTerminalOutcome(
            key,
            comp,
            contract,
            Loc.GetString("nc-store-contract-ghost-role-timeout"),
            ContractObjectiveOutcome.NotAccepted);
    }

    private void HandleGhostRoleTargetResolved(
        (EntityUid Store, string ContractId) key,
        NcStoreComponent comp,
        ContractServerData contract
    )
    {
        if (_objectiveRuntime.ByContract.TryGetValue(key, out var state))
            MarkGhostRoleRoundEndOutcome(
                state,
                GhostRoleRoundEndOutcome.TargetLost,
                Loc.GetString("nc-store-contract-ghost-role-target-lost"));

            FinalizeObjectiveTerminalOutcome(
            key,
            comp,
            contract,
            Loc.GetString("nc-store-contract-ghost-role-target-lost"),
            ContractObjectiveOutcome.TargetLost);
    }

    public bool HasRealtimeContractState(NcStoreComponent comp)
    {
        foreach (var contract in comp.Contracts.Values)
        {
            if (!contract.Taken)
                continue;

            EnsureObjectiveRuntimeDefaults(contract);
            if (contract.Runtime.Failed || contract.Completed)
                continue;

            if (contract.IsGhostRoleObjective ||
                contract.IsTrackedDeliveryObjective ||
                contract.AllowsStoreWorldTurnIn)
                return true;
        }

        return false;
    }

    private bool IsGhostRoleTargetAtStore(EntityUid store, EntityUid target)
    {
        if (!TryComp(store, out TransformComponent? storeXform) ||
            !TryComp(target, out TransformComponent? targetXform))
            return false;

        if (storeXform.MapID != targetXform.MapID)
            return false;

        var storePos = _xform.GetWorldPosition(storeXform);
        var targetPos = _xform.GetWorldPosition(targetXform);
        return (targetPos - storePos).LengthSquared() <=
            NcContractTuning.GhostRoleStoreDeliveryRange * NcContractTuning.GhostRoleStoreDeliveryRange;
    }

    private void SyncGhostRoleObjectiveProgress(EntityUid store, string contractId, ContractServerData contract)
    {
        var key = (store, contractId);
        var runtime = contract.Runtime;

        if (!_objectiveRuntime.ByContract.TryGetValue(key, out var state))
        {
            runtime.GhostRolePendingAcceptance = false;
            runtime.AcceptTimeoutRemainingSeconds = 0;
            runtime.GhostRoleSurvivalRemainingSeconds = 0;
            runtime.StatusHint = string.Empty;
            return;
        }

        if (!state.GhostRoleTaken && state.GhostRoleAcceptDeadline is { } deadline)
        {
            runtime.GhostRolePendingAcceptance = true;
            runtime.AcceptTimeoutRemainingSeconds = Math.Max(
                0,
                (int) Math.Ceiling((deadline - _timing.CurTime).TotalSeconds));
            runtime.GhostRoleSurvivalRemainingSeconds = 0;
            runtime.StatusHint = Loc.GetString("nc-store-contract-ghost-role-hint-waiting");
            runtime.Stage = 0;
            return;
        }

        runtime.GhostRolePendingAcceptance = false;
        runtime.AcceptTimeoutRemainingSeconds = 0;
        SyncGhostRoleSurvivalRemaining(state, runtime);

        if (state.TargetEntity is not { } target || target == EntityUid.Invalid)
        {
            runtime.StatusHint = string.Empty;
            return;
        }

        if (TerminatingOrDeleted(target))
        {
            OnObjectiveTrackedTargetResolved(key, target);
            return;
        }

        if (TryGetObjectiveContract(key, out var comp, out var liveContract) &&
            TryFailGhostRoleTargetIfInvalidOrRotten(key, state, comp, liveContract))
        {
            return;
        }

        runtime.Stage = state.GhostRoleTaken && IsGhostRoleCompletionSatisfied(store, target, contract)
            ? Math.Max(1, runtime.StageGoal)
            : 0;
        runtime.StatusHint = BuildGhostRoleObjectiveStatusHint(store, target, contract);

        TryRetargetGhostRolePinpointersForOwners(key, state);
    }

    private void SyncGhostRoleSurvivalRemaining(ObjectiveRuntimeState state, ContractRuntimeContextData runtime)
    {
        runtime.GhostRoleSurvivalRemainingSeconds = 0;

        if (!state.GhostRoleTaken ||
            state.GhostRoleSurvivalSucceeded ||
            state.GhostRoleSurvivalDeadline is not { } deadline)
        {
            return;
        }

        runtime.GhostRoleSurvivalRemainingSeconds = Math.Max(
            0,
            (int) Math.Ceiling((deadline - _timing.CurTime).TotalSeconds));
    }

    private string BuildGhostRoleObjectiveStatusHint(EntityUid store, EntityUid target, ContractServerData contract)
    {
        if (_contractGhostRoleRotting.IsRotten(target))
            return Loc.GetString("nc-store-contract-ghost-role-target-rotten");

        return contract.Config.GhostRoleCompletionMode switch
        {
            NcGhostRoleCompletionMode.AliveCuffedTurnIn => BuildAliveCuffedGhostRoleStatusHint(store, target),
            NcGhostRoleCompletionMode.DeadBodyTurnIn => BuildDeadBodyGhostRoleStatusHint(store, target),
            _ => Loc.GetString("nc-store-contract-ghost-role-hint-deliver")
        };
    }

    private string BuildAliveCuffedGhostRoleStatusHint(EntityUid store, EntityUid target)
    {
        if (!IsGhostRoleTargetAlive(target))
            return Loc.GetString("nc-store-contract-ghost-role-hint-alive-revive");

        if (!IsGhostRoleTargetCuffed(target))
            return Loc.GetString("nc-store-contract-ghost-role-hint-alive-cuff");

        if (!IsGhostRoleTargetFullyHealed(target))
            return Loc.GetString("nc-store-contract-ghost-role-hint-alive-heal");

        if (!IsGhostRoleTargetAtStore(store, target))
            return Loc.GetString("nc-store-contract-ghost-role-hint-deliver");

        return Loc.GetString("nc-store-contract-ghost-role-hint-alive-ready");
    }

    private string BuildDeadBodyGhostRoleStatusHint(EntityUid store, EntityUid target)
    {
        if (!IsGhostRoleTargetDead(target))
            return Loc.GetString("nc-store-contract-ghost-role-hint-dead-kill");

        if (!IsGhostRoleTargetAtStore(store, target))
            return Loc.GetString("nc-store-contract-ghost-role-hint-dead-deliver");

        return Loc.GetString("nc-store-contract-ghost-role-hint-dead-ready");
    }

    private bool TryFailGhostRoleTargetIfInvalidOrRotten(
        (EntityUid Store, string ContractId) key,
        ObjectiveRuntimeState state,
        NcStoreComponent comp,
        ContractServerData contract)
    {
        if (!state.GhostRoleTaken ||
            state.TargetEntity is not { } target ||
            target == EntityUid.Invalid)
        {
            return false;
        }

        if (TerminatingOrDeleted(target))
        {
            OnObjectiveTrackedTargetResolved(key, target);
            return true;
        }

        if (!_contractGhostRoleRotting.IsRotten(target))
            return false;

        MarkGhostRoleRoundEndOutcome(
            state,
            GhostRoleRoundEndOutcome.TargetRotten,
            Loc.GetString("nc-store-contract-ghost-role-target-rotten"));
        FinalizeObjectiveTerminalOutcome(
            key,
            comp,
            contract,
            Loc.GetString("nc-store-contract-ghost-role-target-rotten"),
            ContractObjectiveOutcome.TargetRotten);
        return true;
    }

    private bool IsGhostRoleTargetReadyForClaim(EntityUid store, EntityUid target, ContractServerData contract)
    {
        return !TerminatingOrDeleted(target) &&
               !_contractGhostRoleRotting.IsRotten(target) &&
               IsGhostRoleCompletionSatisfied(store, target, contract);
    }

    private bool IsGhostRoleSelfClaim(
        EntityUid store,
        string contractId,
        EntityUid user,
        ContractServerData contract)
    {
        if (!contract.IsGhostRoleObjective ||
            !_objectiveRuntime.ByContract.TryGetValue((store, contractId), out var state) ||
            !state.GhostRoleTaken ||
            state.TargetEntity is not { } target ||
            target == EntityUid.Invalid)
        {
            return false;
        }

        if (target == user)
            return true;

        return _contractMind.TryGetMind(target, out var targetMindId, out _) &&
               _contractMind.TryGetMind(user, out var userMindId, out _) &&
               targetMindId == userMindId;
    }

    private bool IsGhostRoleCompletionSatisfied(EntityUid store, EntityUid target, ContractServerData contract)
    {
        if (!IsGhostRoleTargetAtStore(store, target))
            return false;

        return contract.Config.GhostRoleCompletionMode switch
        {
            NcGhostRoleCompletionMode.DeadBodyTurnIn => IsGhostRoleTargetDead(target),
            NcGhostRoleCompletionMode.AliveCuffedTurnIn => IsGhostRoleTargetAlive(target) &&
                                                           IsGhostRoleTargetCuffed(target) &&
                                                           IsGhostRoleTargetFullyHealed(target),
            _ => false
        };
    }

    private bool IsGhostRoleTargetDead(EntityUid target)
    {
        return TryComp(target, out MobStateComponent? mobState) &&
               mobState.CurrentState == MobState.Dead;
    }

    private bool IsGhostRoleTargetAlive(EntityUid target)
    {
        return TryComp(target, out MobStateComponent? mobState) &&
               mobState.CurrentState != MobState.Dead;
    }

    private bool IsGhostRoleTargetCuffed(EntityUid target)
    {
        return TryComp(target, out CuffableComponent? cuffable) &&
               _contractGhostRoleCuffs.IsCuffed((target, cuffable));
    }

    private bool IsGhostRoleTargetFullyHealed(EntityUid target)
    {
        return TryComp(target, out DamageableComponent? damageable) &&
               damageable.TotalDamage <= FixedPoint2.Zero;
    }

    private void OnObjectiveTrackedDamageChanged(EntityUid uid, DamageableComponent component, DamageChangedEvent args)
    {
        if (!_objectiveRuntime.ByTarget.TryGetValue(uid, out var key))
            return;

        if (!TryGetObjectiveContract(key, out _, out var contract) ||
            contract.ExecutionKind != ContractExecutionKind.GhostRoleObjective)
        {
            return;
        }

        UpdateObjectiveContractProgress(key.Store, key.ContractId, contract);

        var ev = new NcContractsChangedEvent();
        RaiseLocalEvent(key.Store, ref ev);
    }

    private bool IsGhostRoleTargetCarriedByUser(EntityUid target, EntityUid user)
    {
        if (TryComp(target, out PullableComponent? pullable) && pullable.Puller == user)
            return true;

        return TryGetContainedEntityRoot(target, out var root) && root == user;
    }
}
