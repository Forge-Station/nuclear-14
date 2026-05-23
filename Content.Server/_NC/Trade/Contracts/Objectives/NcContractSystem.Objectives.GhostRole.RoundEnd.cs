using System.Linq;
using System.Text;
using Content.Server.GameTicking;
using Content.Shared._NC.Trade;
using Content.Shared.GameTicking;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem : EntitySystem
{
    private readonly Dictionary<long, GhostRoleRoundEndRecord> _ghostRoleRoundEndById = new();
    private readonly List<GhostRoleRoundEndRecord> _ghostRoleRoundEndRecords = new();
    private long _ghostRoleRoundEndNextId;

    private void RegisterGhostRoleRoundEndRecord(
        (EntityUid Store, string ContractId) key,
        ContractServerData contract,
        ObjectiveRuntimeState state
    )
    {
        var record = new GhostRoleRoundEndRecord
        {
            Id = ++_ghostRoleRoundEndNextId,
            Store = key.Store,
            ContractId = key.ContractId,
            ContractName = contract.Name,
            RoleName = ResolveContractGhostRoleName(contract.Config, contract),
            RolePrototype = contract.Config.GhostRolePrototype,
            CompletionMode = contract.Config.GhostRoleCompletionMode,
            SurvivalDurationSeconds = contract.Config.GhostRoleSurvivalDurationSeconds,
            Outcome = GhostRoleRoundEndOutcome.WaitingForRole,
            CreatedAt = _timing.CurTime
        };

        state.GhostRoleRoundEndId = record.Id;
        _ghostRoleRoundEndRecords.Add(record);
        _ghostRoleRoundEndById[record.Id] = record;
    }

    private void MarkGhostRoleRoundEndTaken(
        ObjectiveRuntimeState state,
        ContractServerData contract,
        EntityUid target,
        string playerName
    )
    {
        if (!TryGetGhostRoleRoundEndRecord(state, out var record))
            return;

        record.Outcome = GhostRoleRoundEndOutcome.Active;
        record.RoleName = ResolveContractGhostRoleName(contract.Config, contract);
        record.RolePrototype = contract.Config.GhostRolePrototype;
        record.PlayerName = playerName;
        record.CharacterName = TryName(target, out var characterName)
            ? characterName
            : string.Empty;
        record.TakenAt = _timing.CurTime;
    }

    private void MarkGhostRoleRoundEndOutcome(
        ObjectiveRuntimeState state,
        GhostRoleRoundEndOutcome outcome,
        string details = ""
    )
    {
        if (!TryGetGhostRoleRoundEndRecord(state, out var record))
            return;

        if (IsFinalGhostRoleRoundEndOutcome(record.Outcome))
            return;

        record.Outcome = outcome;
        record.Details = details;
        record.FinishedAt = _timing.CurTime;
    }

    private void TryMarkGhostRoleRoundEndClaimed(
        EntityUid store,
        string contractId,
        ContractServerData contract,
        ObjectiveConsumeJournal journal
    )
    {
        if (!contract.IsGhostRoleObjective ||
            !_objectiveRuntime.ByContract.TryGetValue((store, contractId), out var state))
            return;

        var outcome = contract.Config.GhostRoleCompletionMode == NcGhostRoleCompletionMode.AliveCuffedTurnIn
            ? GhostRoleRoundEndOutcome.DeliveredAlive
            : GhostRoleRoundEndOutcome.DeliveredDead;

        if (TryGetGhostRoleRoundEndRecord(state, out var record) &&
            !IsFinalGhostRoleRoundEndOutcome(record.Outcome))
            journal.TrackRoundEnd(record);

        MarkGhostRoleRoundEndOutcome(state, outcome);
    }

    private bool TryGetGhostRoleRoundEndRecord(
        ObjectiveRuntimeState state,
        out GhostRoleRoundEndRecord record
    )
    {
        if (state.GhostRoleRoundEndId > 0 &&
            _ghostRoleRoundEndById.TryGetValue(state.GhostRoleRoundEndId, out record!))
            return true;

        record = default!;
        return false;
    }

    private void OnGhostRoleRoundEndText(RoundEndTextAppendEvent ev)
    {
        RefreshActiveGhostRoleRoundEndRecords();
        if (_ghostRoleRoundEndRecords.Count == 0)
            return;

        var text = new StringBuilder();
        text.AppendLine(Loc.GetString("nc-store-contract-ghost-role-roundend-header"));

        foreach (var record in _ghostRoleRoundEndRecords
            .OrderBy(r => r.CreatedAt)
            .ThenBy(r => r.ContractName, StringComparer.Ordinal))
            text.AppendLine(
                Loc.GetString(
                    "nc-store-contract-ghost-role-roundend-line",
                    ("contract", record.ContractName),
                    ("role", BuildGhostRoleRoundEndRoleName(record)),
                    ("player", BuildGhostRoleRoundEndPlayerName(record)),
                    ("result", BuildGhostRoleRoundEndResult(record))));

        ev.AddLine(text.ToString());
    }

    private void RefreshActiveGhostRoleRoundEndRecords()
    {
        foreach (var (key, state) in _objectiveRuntime.ByContract)
        {
            if (!TryGetGhostRoleRoundEndRecord(state, out var record) ||
                IsFinalGhostRoleRoundEndOutcome(record.Outcome))
                continue;

            if (!TryGetObjectiveContract(key, out _, out var contract) ||
                !contract.Taken ||
                contract.Runtime.Failed)
                continue;

            record.Outcome = state.GhostRoleTaken
                ? GhostRoleRoundEndOutcome.Active
                : GhostRoleRoundEndOutcome.WaitingForRole;
        }
    }

    private string BuildGhostRoleRoundEndRoleName(GhostRoleRoundEndRecord record)
    {
        if (!string.IsNullOrWhiteSpace(record.CharacterName))
            return record.CharacterName;

        if (!string.IsNullOrWhiteSpace(record.RoleName))
            return record.RoleName;

        if (!string.IsNullOrWhiteSpace(record.RolePrototype))
            return record.RolePrototype;

        return Loc.GetString("nc-store-contract-ghost-role-roundend-unknown-role");
    }

    private string BuildGhostRoleRoundEndPlayerName(GhostRoleRoundEndRecord record) =>
        string.IsNullOrWhiteSpace(record.PlayerName)
            ? Loc.GetString("nc-store-contract-ghost-role-roundend-no-player")
            : record.PlayerName;

    private string BuildGhostRoleRoundEndResult(GhostRoleRoundEndRecord record)
    {
        var key = record.Outcome switch
        {
            GhostRoleRoundEndOutcome.WaitingForRole => "nc-store-contract-ghost-role-roundend-result-waiting",
            GhostRoleRoundEndOutcome.Active => "nc-store-contract-ghost-role-roundend-result-active",
            GhostRoleRoundEndOutcome.DeliveredAlive => "nc-store-contract-ghost-role-roundend-result-delivered-alive",
            GhostRoleRoundEndOutcome.DeliveredDead => "nc-store-contract-ghost-role-roundend-result-delivered-dead",
            GhostRoleRoundEndOutcome.RoleSurvived => "nc-store-contract-ghost-role-roundend-result-survived",
            GhostRoleRoundEndOutcome.NotAccepted => "nc-store-contract-ghost-role-roundend-result-not-accepted",
            GhostRoleRoundEndOutcome.TargetLost => "nc-store-contract-ghost-role-roundend-result-target-lost",
            GhostRoleRoundEndOutcome.TargetRotten => "nc-store-contract-ghost-role-roundend-result-target-rotten",
            _ => "nc-store-contract-ghost-role-roundend-result-active"
        };

        return Loc.GetString(
            key,
            ("time", FormatGhostRoleDurationText(record.SurvivalDurationSeconds)),
            ("details", record.Details));
    }

    private void OnGhostRoleRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        _ghostRoleRoundEndRecords.Clear();
        _ghostRoleRoundEndById.Clear();
        _ghostRoleRoundEndNextId = 0;
    }

    private static bool IsFinalGhostRoleRoundEndOutcome(GhostRoleRoundEndOutcome outcome) =>
        outcome is GhostRoleRoundEndOutcome.DeliveredAlive
            or GhostRoleRoundEndOutcome.DeliveredDead
            or GhostRoleRoundEndOutcome.RoleSurvived
            or GhostRoleRoundEndOutcome.NotAccepted
            or GhostRoleRoundEndOutcome.TargetLost
            or GhostRoleRoundEndOutcome.TargetRotten;

    private enum GhostRoleRoundEndOutcome : byte
    {
        WaitingForRole,
        Active,
        DeliveredAlive,
        DeliveredDead,
        RoleSurvived,
        NotAccepted,
        TargetLost,
        TargetRotten
    }

    private sealed class GhostRoleRoundEndRecord
    {
        public string CharacterName = string.Empty;
        public NcGhostRoleCompletionMode CompletionMode;
        public string ContractId = string.Empty;
        public string ContractName = string.Empty;
        public TimeSpan CreatedAt;
        public string Details = string.Empty;
        public TimeSpan? FinishedAt;
        public long Id;
        public GhostRoleRoundEndOutcome Outcome;
        public string PlayerName = string.Empty;
        public string RoleName = string.Empty;
        public string RolePrototype = string.Empty;
        public EntityUid Store;
        public int SurvivalDurationSeconds;
        public TimeSpan? TakenAt;
    }
}
