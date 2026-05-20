using Content.Shared._NC.Trade;
using Content.Shared.Stacks;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem
{
    private void CommitClaimTakeJournal(ClaimTakeJournal journal)
    {
        for (var i = 0; i < journal.PendingDeletes.Count; i++)
        {
            var ent = journal.PendingDeletes[i];
            DeleteFinalEntityBestEffort(ent, "ClaimTake");
        }

        journal.Clear();
    }

    private void RollbackClaimTakeJournal(ClaimTakeJournal journal)
    {
        if (journal.TurnInState != null)
        {
            for (var i = journal.TurnInRestores.Count - 1; i >= 0; i--)
            {
                var restore = journal.TurnInRestores[i];
                if (restore.HadValue)
                    journal.TurnInState.TurnedInByTarget[restore.Key] = restore.PreviousValue;
                else
                    journal.TurnInState.TurnedInByTarget.Remove(restore.Key);
            }
        }

        for (var i = journal.RetrievalCargoRestores.Count - 1; i >= 0; i--)
        {
            var (cargo, key) = journal.RetrievalCargoRestores[i];
            if (Exists(cargo))
                _objectiveRuntime.ByRetrievalCargo[cargo] = key;
        }

        for (var i = journal.StackRestores.Count - 1; i >= 0; i--)
        {
            var (ent, previousCount) = journal.StackRestores[i];
            if (TryComp(ent, out StackComponent? stack))
                _stacks.SetCount(ent, previousCount, stack);
        }

        journal.Clear();
    }

    private sealed class ClaimTakeJournal
    {
        public readonly List<EntityUid> PendingDeletes = new();
        public readonly List<(EntityUid Cargo, (EntityUid Store, string ContractId) Key)> RetrievalCargoRestores = new();
        public readonly List<(EntityUid Ent, int PreviousCount)> StackRestores = new();
        public readonly List<TurnInRestore> TurnInRestores = new();
        public ObjectiveRuntimeState? TurnInState;

        public void TrackStack(EntityUid ent, int previousCount)
        {
            for (var i = 0; i < StackRestores.Count; i++)
            {
                if (StackRestores[i].Ent == ent)
                    return;
            }

            StackRestores.Add((ent, previousCount));
        }

        public void TrackRetrievalCargo(EntityUid cargo, (EntityUid Store, string ContractId) key)
        {
            for (var i = 0; i < RetrievalCargoRestores.Count; i++)
            {
                if (RetrievalCargoRestores[i].Cargo == cargo)
                    return;
            }

            RetrievalCargoRestores.Add((cargo, key));
        }

        public void TrackTurnIn(
            ObjectiveRuntimeState state,
            (string TargetItem, PrototypeMatchMode MatchMode) key)
        {
            TurnInState ??= state;

            for (var i = 0; i < TurnInRestores.Count; i++)
            {
                if (TurnInRestores[i].Key == key)
                    return;
            }

            var hadValue = state.TurnedInByTarget.TryGetValue(key, out var previousValue);
            TurnInRestores.Add(new TurnInRestore(key, hadValue, previousValue));
        }

        public void Clear()
        {
            PendingDeletes.Clear();
            RetrievalCargoRestores.Clear();
            StackRestores.Clear();
            TurnInRestores.Clear();
            TurnInState = null;
        }
    }

    private readonly record struct TurnInRestore(
        (string TargetItem, PrototypeMatchMode MatchMode) Key,
        bool HadValue,
        int PreviousValue);
}
