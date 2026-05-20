using Robust.Shared.Prototypes;

namespace Content.Shared._NC.Trade;

/// <summary>
/// Retrieval route layout: content defines cargo, route and reward.
/// Retrieval is spawned cargo delivery: the contract creates cargo, then the player moves that cargo along a route.
/// Existing-world item turn-in belongs to Supply, not Retrieval.
/// </summary>
[Prototype("ncRetrievalContract")]
public sealed partial class NcRetrievalContractPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;

    [DataField("name", required: true)]
    public string Name { get; private set; } = string.Empty;

    [DataField("description")]
    public string Description { get; private set; } = string.Empty;

    [DataField("repeatable")]
    public bool Repeatable { get; private set; } = true;

    /// <summary>Optional entity prototype id used only as a UI icon fallback for the contract card.</summary>
    [DataField("icon")]
    public string Icon { get; private set; } = string.Empty;

    /// <summary>Retrieval cargo spawned by the route source. This replaces Retrieval Stage 1/2 'targets'.</summary>
    [DataField("cargo", required: true)]
    public List<NcSupplyTargetEntry> Cargo { get; private set; } = new();

    /// <summary>The route preset defines source/destination/proof/guidance. Required for Retrieval Route layout.</summary>
    [DataField("route", required: true)]
    public ProtoId<NcRetrievalRoutePresetPrototype> Route { get; private set; }

    /// <summary>Unified Retrieval rewards. Use type: Currency, Item or Pool with count.</summary>
    [DataField("reward", required: true)]
    public List<NcSupplyRewardEntry> Reward { get; private set; } = new();

    /// <summary>Optional extension conditions evaluated by registered server-side handlers.</summary>
    [DataField("conditions")]
    public List<ContractConditionDef> Conditions { get; private set; } = new();

    // Retrieval is intentionally strict: old targets/targetCount/spawn fields are not represented here.
    // Invalid old YAML is blocked by nc_trade_core_audit.py before prototype load.
}
