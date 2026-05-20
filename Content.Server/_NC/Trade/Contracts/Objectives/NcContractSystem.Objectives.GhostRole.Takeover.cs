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
}
