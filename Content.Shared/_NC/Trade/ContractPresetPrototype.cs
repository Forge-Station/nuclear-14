using Robust.Shared.Prototypes;

namespace Content.Shared._NC.Trade;

[Prototype("storeContractsPreset")]
public sealed partial class StoreContractsPresetPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    ///     Список всех возможных контрактов (слотов нет — автоматы сами решают, сколько брать).
    /// </summary>
    [DataField("contracts", required: true)]
    public Dictionary<string, ContractPresetEntry> Contracts { get; set; } = new();

    [DataDefinition]
    public sealed partial class ContractPresetEntry
    {
        /// <summary> Уникальный ID контракта (иначе берётся ключ словаря). </summary>
        [DataField("id")]
        public string? Id;

        /// <summary> Какой предмет надо сдать. </summary>
        [DataField("targetItem", required: true)]
        public string TargetItem = string.Empty;

        /// <summary> Сколько таких предметов нужно. </summary>
        [DataField("required")]
        public int Required = 1;

        /// <summary> Денежная награда. </summary>
        [DataField("reward")]
        public int Reward = 0;

        /// <summary> Валюта для денежной награды (StackPrototype.ID). </summary>
        [DataField("rewardCurrency")]
        public string RewardCurrency = string.Empty;

        /// <summary> Сложность для UI. </summary>
        [DataField("difficulty")]
        public string Difficulty = "Easy";

        /// <summary> Описание (показывается в UI). </summary>
        [DataField("description")]
        public string Description = string.Empty;


        /// <summary> Прототип сущности предмета, который дать в награду. </summary>
        [DataField("rewardItem")]
        public string? RewardItem;

        /// <summary> Количество таких предметов. </summary>
        [DataField("rewardItemCount")]
        public int RewardItemCount;
    }
}
