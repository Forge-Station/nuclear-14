using Content.Server.Ghost.Roles.Components;
using Content.Shared._NC.Trade;
using Content.Shared.Customization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem : EntitySystem
{
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
}
