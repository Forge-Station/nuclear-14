using System;
using System.Collections.Generic;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._NC.Trade;

[Serializable, NetSerializable]
public sealed class StoreListingData
{
    public string Category = string.Empty;
    public string CurrencyId = string.Empty;
    public string Id = string.Empty;
    public StoreMode Mode;
    public int Owned;
    public int Price;
    public string ProductEntity = string.Empty;
    public int Remaining = -1;

    public StoreListingData() { }

    public StoreListingData(
        string id,
        string productEntity,
        int price,
        string category,
        string currencyId,
        StoreMode mode,
        int owned = 0,
        int remaining = -1
    )
    {
        Id = id;
        ProductEntity = productEntity;
        Price = price;
        Category = category;
        CurrencyId = currencyId;
        Mode = mode;
        Owned = owned;
        Remaining = remaining;
    }
}

// ============================================================
// Contracts - Rewards (Blueprint -> Baked result)
// ============================================================

[Serializable, NetSerializable]
public enum StoreRewardType : byte
{
    Item,
    Currency,
    Pool
}

[DataDefinition]
[Serializable, NetSerializable]
public sealed partial class ContractRewardDef
{
    [DataField("type")]
    public StoreRewardType Type { get; set; } = StoreRewardType.Item;

    [DataField("id")]
    public string Id { get; set; } = string.Empty;

    // amount: 5 OR amount: {min: 1, max: 5}
    [DataField("amount")]
    public IntRange Amount { get; set; } = IntRange.Fixed(1);

    // prob: 0..1
    [DataField("prob")]
    public float Probability { get; set; } = 1.0f;

    [DataField("weight")]
    public int Weight { get; set; } = 1;

    // max: 0 => unlimited
    [DataField("max")]
    public int MaxRepeats { get; set; } = 0;

    // Nested pool options
    [DataField("options")]
    public List<ContractRewardDef>? Options { get; set; }
}

[Serializable, NetSerializable]
public readonly record struct ContractRewardData(StoreRewardType Type, string Id, int Amount);

// ============================================================
// Contracts - Targets / Server contract snapshot
// ============================================================

[Serializable]
public sealed class ContractTargetServerData
{
    [DataField("match")]
    public PrototypeMatchMode MatchMode = PrototypeMatchMode.Exact;

    public string TargetItem { get; set; } = string.Empty;
    public int Required { get; set; }
    public int Progress { get; set; }
}

[Serializable]
public sealed class ContractServerData
{
    [DataField("match")]
    public PrototypeMatchMode MatchMode = PrototypeMatchMode.Exact;

    public List<ContractTargetServerData> Targets { get; set; } = new();

    // OLD fields kept for UI compatibility (StoreStructuredSystem reads these):contentReference[oaicite:8]{index=8}
    public string TargetItem { get; set; } = string.Empty;
    public int Required { get; set; }
    public int Progress { get; set; }

    public bool Repeatable { get; set; } = true;

    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public int Reward { get; set; }
    public string RewardCurrency { get; set; } = string.Empty;
    public string? RewardItem { get; set; }
    public int RewardItemCount { get; set; }

    public Dictionary<string, int> RewardCurrencies { get; set; } = new();
    public Dictionary<string, int> RewardItems { get; set; } = new();

    public string Difficulty { get; set; } = "Easy";
    public string Description { get; set; } = string.Empty;

    // NEW: baked rewards list (used by new NcContractSystem)
    public List<ContractRewardData> Rewards { get; set; } = new();

    public bool Completed
    {
        get
        {
            if (Targets.Count > 0)
            {
                var any = false;
                foreach (var t in Targets)
                {
                    if (t.Required <= 0)
                        continue;

                    any = true;
                    if (t.Progress < t.Required)
                        return false;
                }

                return any;
            }

            return Required > 0 && Progress >= Required;
        }
    }
}

[Serializable, NetSerializable]
public sealed class ContractTargetClientData
{
    [DataField("match")]
    public PrototypeMatchMode MatchMode = PrototypeMatchMode.Exact;

    public ContractTargetClientData() { }

    public ContractTargetClientData(string targetItem, int required, int progress)
    {
        TargetItem = targetItem;
        Required = required;
        Progress = progress;
    }

    public string TargetItem { get; set; } = string.Empty;
    public int Required { get; set; }
    public int Progress { get; set; }
}

[Serializable, NetSerializable]
public enum StoreMode
{
    Buy,
    Sell,
    Exchange
}
