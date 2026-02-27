using Content.Shared.Damage;
using Robust.Shared.GameStates;

namespace Content.Shared._N14.ArmorMod;

/// <summary>
/// Marks an item as an armor modification.
/// When inserted into a slot on <see cref="ArmorModifiableComponent"/>,
/// it alters the armor's damage modifiers.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ArmorModComponent : Component
{
    /// <summary>
    /// The modifiers this mod applies to the armor.
    /// Coefficients are multiplied with the armor's base coefficients:
    ///   value of 0.9 means an additional 10% protection on top of the base.
    /// FlatReductions are added to the armor's base flat reductions.
    /// </summary>
    [DataField(required: true), AutoNetworkedField]
    public DamageModifierSet Modifiers = new();
}
