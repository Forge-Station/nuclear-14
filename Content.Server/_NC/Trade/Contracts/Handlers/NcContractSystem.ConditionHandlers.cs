using Content.Server._NC.Sponsor;
using Content.Server.Ghost.Roles.Components;
using Content.Server.Players.PlayTimeTracking;
using Content.Server.Preferences.Managers;
using Content.Shared.Clothing.Loadouts.Prototypes;
using Content.Shared.Customization.Systems;
using Content.Shared._NC.Trade;
using Content.Shared.Players;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Shared.Configuration;
using Robust.Shared.Player;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private const string GhostRoleRequirementsCondition = "ghostRoleRequirements";

    private readonly Dictionary<string, IContractConditionHandler> _conditionHandlers = new(StringComparer.Ordinal);

    private void InitializeConditionHandlers()
    {
        _conditionHandlers.Clear();
        RegisterConditionHandler(new GhostRoleRequirementsConditionHandler());
        RegisterAdditionalConditionHandlers();
    }

    // Downstream projects can implement this in another NcContractSystem partial file
    // and register reusable contract conditions without extending objective core logic.
    partial void RegisterAdditionalConditionHandlers();

    private void RegisterConditionHandler(IContractConditionHandler handler)
    {
        if (_conditionHandlers.ContainsKey(handler.Type))
        {
            Sawmill.Warning($"[Contracts] Duplicate condition handler '{handler.Type}'; replacing previous handler.");
        }

        _conditionHandlers[handler.Type] = handler;
    }

    private bool TryEvaluateContractCondition(
        string type,
        ContractConditionContext context,
        out string? failure)
    {
        failure = null;
        if (!_conditionHandlers.TryGetValue(type, out var handler))
        {
            failure = $"No contract condition handler registered for '{type}'.";
            return false;
        }

        return handler.IsSatisfied(this, context, out failure);
    }

    private bool TryEvaluateContractCondition(
        ContractConditionDef condition,
        ContractConditionContext context,
        out string? failure)
    {
        failure = null;
        var type = condition.Type;
        if (string.IsNullOrWhiteSpace(type))
        {
            failure = "Contract condition has no type.";
            return false;
        }

        if (!_conditionHandlers.TryGetValue(type, out var handler))
        {
            failure = $"No contract condition handler registered for '{type}'.";
            return false;
        }

        var satisfied = handler.IsSatisfied(this, context, out failure);
        if (condition.Invert)
        {
            if (!satisfied)
            {
                failure = null;
                return true;
            }

            failure = string.IsNullOrWhiteSpace(condition.Id)
                ? $"Contract condition '{type}' is inverted and currently satisfied."
                : $"Contract condition '{type}:{condition.Id}' is inverted and currently satisfied.";
            return false;
        }

        if (satisfied)
            return true;

        if (string.IsNullOrWhiteSpace(failure))
            failure = string.IsNullOrWhiteSpace(condition.Id)
                ? $"Contract condition '{type}' is not satisfied."
                : $"Contract condition '{type}:{condition.Id}' is not satisfied.";

        return false;
    }

    private bool TryEvaluateContractConditions(
        ContractConditionPhase phase,
        EntityUid store,
        EntityUid user,
        string contractId,
        ContractServerData contract,
        out string? failure)
    {
        failure = null;
        for (var i = 0; i < contract.Conditions.Count; i++)
        {
            var condition = contract.Conditions[i];
            if (!ContractConditionApplies(condition.Phase, phase))
                continue;

            var context = new ContractConditionContext(
                Store: store,
                User: user,
                ContractId: contractId,
                Contract: contract,
                Condition: condition,
                Phase: phase);

            if (TryEvaluateContractCondition(condition, context, out failure))
                continue;

            return false;
        }

        return true;
    }

    private static bool ContractConditionApplies(ContractConditionPhase configured, ContractConditionPhase phase)
    {
        return configured switch
        {
            ContractConditionPhase.Always => true,
            ContractConditionPhase.TakeAndClaim => phase is ContractConditionPhase.Take or ContractConditionPhase.Claim,
            _ => configured == phase
        };
    }

    private static List<ContractConditionDef> CloneContractConditions(IReadOnlyList<ContractConditionDef> conditions)
    {
        var result = new List<ContractConditionDef>(conditions.Count);
        for (var i = 0; i < conditions.Count; i++)
        {
            var condition = conditions[i];
            result.Add(new ContractConditionDef
            {
                Type = condition.Type,
                Id = condition.Id,
                Phase = condition.Phase,
                Invert = condition.Invert,
                Args = new Dictionary<string, string>(condition.Args, StringComparer.Ordinal)
            });
        }

        return result;
    }

    private bool TryValidateContractConditions(string ownerId, IReadOnlyList<ContractConditionDef> conditions)
    {
        var valid = true;
        for (var i = 0; i < conditions.Count; i++)
        {
            var condition = conditions[i];
            if (string.IsNullOrWhiteSpace(condition.Type))
            {
                Sawmill.Warning($"[Contracts] Contract '{ownerId}' conditions[{i}] has no type.");
                valid = false;
                continue;
            }

            if (!_conditionHandlers.ContainsKey(condition.Type))
            {
                Sawmill.Warning(
                    $"[Contracts] Contract '{ownerId}' conditions[{i}] references unregistered condition handler '{condition.Type}'.");
                valid = false;
            }
        }

        return valid;
    }

    private readonly record struct ContractConditionContext(
        ICommonSession? Player = null,
        EntityUid Spawner = default,
        NcContractGhostRoleSpawnerComponent? GhostRoleSpawner = null,
        GhostRoleComponent? GhostRole = null,
        EntityUid Store = default,
        EntityUid User = default,
        string ContractId = "",
        ContractServerData? Contract = null,
        ContractConditionDef? Condition = null,
        ContractConditionPhase Phase = ContractConditionPhase.Always);

    private interface IContractConditionHandler
    {
        string Type { get; }

        bool IsSatisfied(
            NcContractSystem system,
            ContractConditionContext context,
            out string? failure);
    }

    private sealed class GhostRoleRequirementsConditionHandler : IContractConditionHandler
    {
        public string Type => GhostRoleRequirementsCondition;

        public bool IsSatisfied(
            NcContractSystem system,
            ContractConditionContext context,
            out string? failure)
        {
            failure = null;

            if (context.Player == null ||
                context.GhostRoleSpawner == null ||
                context.GhostRole == null ||
                context.GhostRole.Taken ||
                system.MetaData(context.Spawner).EntityPaused)
            {
                return false;
            }

            var requirements = context.GhostRoleSpawner.Requirements;
            if (requirements.Count == 0)
                return true;

            if (!system._contractGhostRolePlayTime.TryGetTrackerTimes(context.Player, out var playTimes))
            {
                Sawmill.Error($"Unable to check contract ghost role requirements for {context.Player}.");
                playTimes = new Dictionary<string, TimeSpan>();
            }

            var selectedCharacter = system._contractGhostRolePrefs.GetPreferences(context.Player.UserId).SelectedCharacter;
            var profile = selectedCharacter as HumanoidCharacterProfile
                ?? HumanoidCharacterProfile.DefaultWithSpecies();
            var isWhitelisted = context.Player.ContentData()?.Whitelisted ?? false;

            if (system._contractGhostRoleRequirements.CheckRequirementsValid(
                    requirements,
                    new JobPrototype(),
                    profile,
                    playTimes,
                    isWhitelisted,
                    new LoadoutPrototype(),
                    system.EntityManager,
                    system._prototypes,
                    system._contractGhostRoleConfig,
                    system._contractGhostRoleSponsor,
                    out var reasons))
            {
                return true;
            }

            failure = system.BuildContractGhostRoleRequirementsFailureMessage(reasons);
            return false;
        }
    }
}
