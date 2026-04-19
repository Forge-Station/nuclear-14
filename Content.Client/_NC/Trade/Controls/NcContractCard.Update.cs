using Content.Shared._NC.Trade;

namespace Content.Client._NC.Trade.Controls;

public sealed partial class NcContractCard
{
    private static int ComputePresentationHash(
        ContractClientData data,
        int skipCost,
        string skipCurrency,
        int skipBalance)
    {
        unchecked
        {
            var hash = data.ComputeFingerprint();
            hash = MixInt(hash, skipCost);
            hash = MixString(hash, skipCurrency);
            hash = MixInt(hash, skipBalance);
            return hash;
        }
    }

    private const int FnvPrime = 16777619;

    private static int MixInt(int hash, int value)
    {
        unchecked
        {
            var h = hash;
            h = (h ^ (value & 0xFF)) * FnvPrime;
            h = (h ^ ((value >> 8) & 0xFF)) * FnvPrime;
            h = (h ^ ((value >> 16) & 0xFF)) * FnvPrime;
            h = (h ^ ((value >> 24) & 0xFF)) * FnvPrime;
            return h;
        }
    }

    private static int MixString(int hash, string? value)
    {
        unchecked
        {
            if (value == null)
                return MixInt(hash, -1);

            var h = hash;
            for (var i = 0; i < value.Length; i++)
                h = (h ^ value[i]) * FnvPrime;

            return MixInt(h, value.Length);
        }
    }
}
