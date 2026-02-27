using Content.Shared.Damage;
using Robust.Shared.GameStates;

namespace Content.Shared._N14.ArmorMod;

/// <summary>
/// Marks armor as capable of accepting modifications through item slots.
/// When an item with <see cref="ArmorModComponent"/> is inserted into one of the
/// listed <see cref="ModSlots"/>, the armor's damage modifiers are recalculated
/// as: base modifiers combined with all installed mods.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ArmorModifiableComponent : Component
{
    /// <summary>
    /// The IDs of item slots (from <see cref="Content.Shared.Containers.ItemSlots.ItemSlotsComponent"/>)
    /// that accept armor modifications. Must match the slot dictionary keys.
    /// </summary>
    [DataField]
    public List<string> ModSlots = new();

    /// <summary>
    /// The armor's base modifiers before any mods are applied.
    /// Automatically populated from <see cref="Content.Shared.Armor.ArmorComponent.Modifiers"/>
    /// on map init. Do not set this manually in YAML.
    /// </summary>
    [DataField]
    public DamageModifierSet BaseModifiers = new();

    /// <summary>
    /// Whether the mod slots are currently locked (require a tool to modify).
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool SlotsLocked = false;

    /// <summary>
    /// The tool quality required to lock/unlock mod slots.
    /// </summary>
    [DataField]
    public string LockToolQuality = "Screwing";
}
