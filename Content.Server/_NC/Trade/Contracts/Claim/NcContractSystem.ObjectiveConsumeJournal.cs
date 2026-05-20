using System;
using System.Collections.Generic;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private void CommitObjectiveConsumeJournal(ObjectiveConsumeJournal journal)
    {
        for (var i = 0; i < journal.PendingDeletes.Count; i++)
            DeleteFinalEntityBestEffort(journal.PendingDeletes[i], "ObjectiveConsume");

        journal.Clear();
    }

    private void RollbackObjectiveConsumeJournal(ObjectiveConsumeJournal journal)
    {
        for (var i = journal.RoundEndRestores.Count - 1; i >= 0; i--)
        {
            var restore = journal.RoundEndRestores[i];
            restore.Record.Outcome = restore.Outcome;
            restore.Record.Details = restore.Details;
            restore.Record.FinishedAt = restore.FinishedAt;
        }

        for (var i = journal.HuntBodyRestores.Count - 1; i >= 0; i--)
        {
            var restore = journal.HuntBodyRestores[i];
            restore.State.HuntBodyEntity = restore.PreviousHuntBodyEntity;
            RemoveSpawnedHuntTarget(restore.State, restore.Body);

            for (var j = 0; j < restore.TargetIndexes.Length; j++)
            {
                var index = Math.Clamp(restore.TargetIndexes[j], 0, restore.State.HuntSpawnedTargets.Count);
                restore.State.HuntSpawnedTargets.Insert(index, restore.Body);
            }
        }

        for (var i = journal.ProofStateRestores.Count - 1; i >= 0; i--)
        {
            var restore = journal.ProofStateRestores[i];
            restore.State.ProofEntity = restore.PreviousProofEntity;
        }

        for (var i = journal.ProofIndexRestores.Count - 1; i >= 0; i--)
        {
            var restore = journal.ProofIndexRestores[i];
            if (restore.HadValue)
                _objectiveRuntime.ByProof[restore.Proof] = restore.PreviousKey;
            else
                _objectiveRuntime.ByProof.Remove(restore.Proof);
        }

        journal.Clear();
    }

    private void DeleteFinalEntityBestEffort(EntityUid ent, string context)
    {
        if (ent == EntityUid.Invalid || !Exists(ent))
            return;

        try
        {
            Del(ent);
        }
        catch (Exception e)
        {
            Sawmill.Error($"[{context}] Failed to delete final entity {ToPrettyString(ent)}: {e}");
        }
    }

    private sealed class ObjectiveConsumeJournal
    {
        public readonly List<EntityUid> PendingDeletes = new();
        public readonly List<ProofStateRestore> ProofStateRestores = new();
        public readonly List<ProofIndexRestore> ProofIndexRestores = new();
        public readonly List<HuntBodyRestore> HuntBodyRestores = new();
        public readonly List<RoundEndRestore> RoundEndRestores = new();

        public void TrackProofState(ObjectiveRuntimeState state, EntityUid proof)
        {
            if (state.ProofEntity != proof)
                return;

            for (var i = 0; i < ProofStateRestores.Count; i++)
            {
                if (ReferenceEquals(ProofStateRestores[i].State, state))
                    return;
            }

            ProofStateRestores.Add(new ProofStateRestore(state, state.ProofEntity));
            state.ProofEntity = null;
        }

        public void TrackProofIndex(
            EntityUid proof,
            Dictionary<EntityUid, (EntityUid Store, string ContractId)> proofIndex)
        {
            for (var i = 0; i < ProofIndexRestores.Count; i++)
            {
                if (ProofIndexRestores[i].Proof == proof)
                    return;
            }

            var hadValue = proofIndex.TryGetValue(proof, out var previousKey);
            ProofIndexRestores.Add(new ProofIndexRestore(proof, hadValue, previousKey));
        }

        public void TrackHuntBody(ObjectiveRuntimeState state, EntityUid body)
        {
            for (var i = 0; i < HuntBodyRestores.Count; i++)
            {
                if (ReferenceEquals(HuntBodyRestores[i].State, state) &&
                    HuntBodyRestores[i].Body == body)
                {
                    return;
                }
            }

            HuntBodyRestores.Add(new HuntBodyRestore(
                state,
                body,
                state.HuntBodyEntity,
                CaptureTargetIndexes(state.HuntSpawnedTargets, body)));
        }

        public void TrackRoundEnd(GhostRoleRoundEndRecord record)
        {
            for (var i = 0; i < RoundEndRestores.Count; i++)
            {
                if (ReferenceEquals(RoundEndRestores[i].Record, record))
                    return;
            }

            RoundEndRestores.Add(new RoundEndRestore(
                record,
                record.Outcome,
                record.Details,
                record.FinishedAt));
        }

        public void Clear()
        {
            PendingDeletes.Clear();
            ProofStateRestores.Clear();
            ProofIndexRestores.Clear();
            HuntBodyRestores.Clear();
            RoundEndRestores.Clear();
        }

        private static int[] CaptureTargetIndexes(List<EntityUid> targets, EntityUid body)
        {
            List<int>? indexes = null;
            for (var i = 0; i < targets.Count; i++)
            {
                if (targets[i] != body)
                    continue;

                indexes ??= new();
                indexes.Add(i);
            }

            return indexes?.ToArray() ?? Array.Empty<int>();
        }
    }

    private readonly record struct ProofStateRestore(
        ObjectiveRuntimeState State,
        EntityUid? PreviousProofEntity);

    private readonly record struct ProofIndexRestore(
        EntityUid Proof,
        bool HadValue,
        (EntityUid Store, string ContractId) PreviousKey);

    private readonly record struct HuntBodyRestore(
        ObjectiveRuntimeState State,
        EntityUid Body,
        EntityUid? PreviousHuntBodyEntity,
        int[] TargetIndexes);

    private readonly record struct RoundEndRestore(
        GhostRoleRoundEndRecord Record,
        GhostRoleRoundEndOutcome Outcome,
        string Details,
        TimeSpan? FinishedAt);
}
