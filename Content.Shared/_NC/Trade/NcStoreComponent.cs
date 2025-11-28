using Robust.Shared.GameStates;

namespace Content.Shared._NC.Trade;

[RegisterComponent, NetworkedComponent]
public sealed partial class NcStoreComponent : Component
{
    [DataField("categories")]
    public List<string> Categories = new();

    [DataField("currencyWhitelist")]
    public List<string> CurrencyWhitelist = new();

    public EntityUid? CurrentUser = null;

    // Лоты магазина (загружаются из StorePresetStructuredPrototype)
    [DataField("listings")]
    public List<StoreListingPrototype> Listings = new();

    // Пресет магазина (как уже было)
    [DataField("preset")]
    public string? Preset;

    // 🔹 Имя пресета контрактов из YAML:
    // contracts: caravan_contracts
    [DataField("contracts")]
    public string? ContractsPreset;

    // 🔹 Рантайм-словарь контрактов. В YAML его НЕ трогаем.
    public Dictionary<string, ContractServerData> Contracts { get; } = new();

    [DataField("access")]
    public List<List<string>>? Access;
}
