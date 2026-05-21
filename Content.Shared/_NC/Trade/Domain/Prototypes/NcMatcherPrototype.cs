using Robust.Shared.Prototypes;
using Robust.Shared.Utility;


namespace Content.Shared._NC.Trade;


/// <summary>
///     Phase M: a "matcher" groups multiple entity prototypes into a single logical
///     catalog entry / contract target. Lets the YAML author express "any bread-like item" or
///     "any basic ingot" as one listing row with a custom name/description/sprite, instead of
///     enumerating a dozen separate entries.
///     Usage in YAML:
///     - type: ncMatcher
///     id: NcMatcherBreadLike
///     name: "Хлебобулочное"
///     description: "Любой хлеб, булки, рогалики"
///     sprite:
///     sprite: Objects/Consumable/Food/Baked/bread.rsi
///     state: plain
///     items:                  # strict list of prototype IDs
///     - FoodBreadLoaf
///     - FoodBreadBaguette
///     Semantics:
///     - items — used for EVERY spawn context (Buy-listings, Hunt-targets, spawn-delivery)
///     AND for match-checking brought items.
///     A matcher used in a spawn context must have at least one item in <see cref="Items" />.
///     Tag matching is represented by PrototypeMatchMode.Tag and standalone ncTradeTag targets.
/// </summary>
[Prototype("ncMatcher")]
public sealed partial class NcMatcherPrototype : IPrototype
{
    /// <summary>Display name shown in store UI and contract cards.</summary>
    [DataField("name", required: true)]
    public string Name { get; private set; } = string.Empty;

    /// <summary>Optional longer description shown in tooltips.</summary>
    [DataField("description")]
    public string Description { get; private set; } = string.Empty;

    /// <summary>Icon shown in store UI. If null, the UI falls back to the first entry of Items.</summary>
    [DataField("sprite")]
    public SpriteSpecifier? Sprite { get; private set; }

    /// <summary>
    ///     Prototype IDs this matcher resolves to for spawn AND for match-check. Used in:
    ///     - Buy-listings: random pick spawned on purchase.
    ///     - Hunt-contracts: each mob spawned is randomly picked from here (may repeat).
    ///     - Delivery with spawnItems: items spawned for the player (random picks, may repeat).
    ///     - Sell-listings: entity matches if its prototype ID is in this list.
    ///     - Delivery turn-in: delivered entity matches if its prototype ID is in this list.
    /// </summary>
    [DataField("items")]
    public List<string> Items { get; private set; } = new();

    [IdDataField] public string ID { get; private set; } = default!;
}
