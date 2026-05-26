using Content.Shared._NC.Trade;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem : EntitySystem
{
    public void RefillContractsForStore(EntityUid uid, NcStoreComponent comp, string? ignoredContractId = null)
    {
        if (!TryResolveContractPreset(uid, comp, out var preset))
            return;

        if (preset.ContractOffers is { Groups.Count: > 0, } offers)
            RefillContractsForStoreOffers(uid, comp, offers, ignoredContractId);
        else
        {
            Sawmill.Warning(
                $"[Contracts] Contract preset '{preset.ID}' has no contractOffers groups; no offers generated.");
        }
    }

    private bool TryResolveContractPreset(
        EntityUid uid,
        NcStoreComponent comp,
        out StoreContractsPresetPrototype preset
    )
    {
        preset = default!;

        if (!_prototypes.TryIndex(comp.Profile, out var profile))
        {
            Sawmill.Warning($"[Contracts] Store profile '{comp.Profile}' not found for {ToPrettyString(uid)}.");
            return false;
        }

        if (profile.Contracts == null)
            return false;

        if (!_prototypes.TryIndex(profile.Contracts.Value, out var resolvedPreset))
        {
            Sawmill.Warning(
                $"[Contracts] Contract profile '{profile.Contracts.Value}' not found for store profile '{profile.ID}'.");
            return false;
        }

        preset = resolvedPreset;
        return true;
    }

    private static int SaturatingAdd(int left, int right)
    {
        if (left <= 0)
            return Math.Max(0, right);
        if (right <= 0)
            return left;

        var sum = (long) left + right;
        return sum >= int.MaxValue ? int.MaxValue : (int) sum;
    }

    private bool TryPickAndRemoveWeighted(
        List<ContractPoolCandidate> list,
        out ContractPoolCandidate picked
    )
    {
        picked = default!;

        var total = 0;
        for (var i = 0; i < list.Count; i++)
        {
            var w = list[i].Weight;
            if (w <= 0)
                continue;

            total = SaturatingAdd(total, w);
        }

        if (total <= 0)
            return false;

        var roll = _random.Next(total);

        for (var i = 0; i < list.Count; i++)
        {
            var w = list[i].Weight;
            if (w <= 0)
                continue;

            roll -= w;
            if (roll >= 0)
                continue;

            picked = list[i];

            var last = list.Count - 1;
            list[i] = list[last];
            list.RemoveAt(last);
            return true;
        }

        return false;
    }
}
