using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._NC.Trade;

/// <summary>
/// Phase M: a "matcher" groups multiple entity prototypes and/or tags into a single logical
/// catalog entry / contract target. Lets the YAML author express "any bread-like item" or
/// "any basic ingot" as one listing row with a custom name/description/sprite, instead of
/// enumerating a dozen separate entries.
///
/// Usage in YAML:
///   - type: ncMatcher
///     id: NcMatcherBreadLike
///     name: "Хлебобулочное"
///     description: "Любой хлеб, булки, рогалики"
///     sprite:
///       sprite: Objects/Consumable/Food/Baked/bread.rsi
///       state: plain
///     items:                  # strict list of prototype IDs
///       - FoodBreadLoaf
///       - FoodBreadBaguette
///     tags:                   # wider net — any entity carrying any of these tags
///       - BreadLike
///
/// Semantics:
///   - items — used for EVERY spawn context (Buy-listings, Hunt-targets, spawn-delivery)
///             AND for match-checking brought items.
///   - tags  — used ONLY for match-checking brought items (Sell-listings, Delivery turn-in).
///             tags are never used for spawning — a tag can't uniquely identify a prototype.
///
/// A matcher used in a spawn context must have at least one item in <see cref="Items"/>.
/// A matcher with only tags is valid but only for Sell/Delivery turn-in, not for Buy/Hunt.
/// The store loader validates this and skips invalid listings with a warning.
/// </summary>
[Prototype("ncMatcher")]
public sealed partial class NcMatcherPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;

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
    /// Prototype IDs this matcher resolves to for spawn AND for match-check. Used in:
    ///   - Buy-listings: random pick spawned on purchase.
    ///   - Hunt-contracts: each mob spawned is randomly picked from here (may repeat).
    ///   - Delivery with spawnItems: items spawned for the player (random picks, may repeat).
    ///   - Sell-listings: entity matches if its prototype ID is in this list.
    ///   - Delivery turn-in: delivered entity matches if its prototype ID is in this list.
    /// </summary>
    [DataField("items")]
    public List<string> Items { get; private set; } = new();

    /// <summary>
    /// Tag names. An entity matches this matcher if it carries any one of these tags on its
    /// TagComponent. Used ONLY for match-check on brought items (Sell-listings, Delivery turn-in).
    /// NOT used for spawn — a tag doesn't uniquely identify a prototype to spawn.
    /// </summary>
    [DataField("tags")]
    public List<string> Tags { get; private set; } = new();
}
