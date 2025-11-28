using Robust.Shared.Prototypes;

namespace Content.Shared._NC.Trade;

[Prototype("storeContractsPreset")]
public sealed partial class StoreContractsPresetPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("contracts", required: true)]
    public Dictionary<string, ContractPresetEntry> Contracts { get; set; } = new();

    [DataDefinition]
    public sealed partial class ContractPresetEntry
    {
        [DataField("id")]
        public string? Id;

        [DataField("targetItem", required: true)]
        public string TargetItem = string.Empty;

        [DataField("required")]
        public int Required = 1;

        [DataField("reward")]
        public int Reward = 0;

        [DataField("rewardCurrency")]
        public string RewardCurrency = string.Empty;

        [DataField("difficulty")]
        public string Difficulty = "Easy";

        // 🔹 Описание контракта (то, что хочешь видеть в UI)
        [DataField("description")]
        public string Description = string.Empty;
    }
}
