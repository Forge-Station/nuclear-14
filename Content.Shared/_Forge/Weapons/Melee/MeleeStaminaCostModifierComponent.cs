using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Weapons.Melee;

/// <summary>
///     Multiplies the stamina cost of heavy melee attacks made by this entity.
///     Used by perks (persistent <see cref="HeavyCostMultiplier"/>) and chem
///     effects (temporary <see cref="Sources"/> that rewrite the multiplier while active).
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class MeleeStaminaCostModifierComponent : Component
{
    /// <summary>
    ///     Current multiplier applied to heavy melee stamina cost.
    ///     Values below 1 make attacks cheaper; 1 means no change.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float HeavyCostMultiplier = 1f;

    /// <summary>
    ///     Active chem sources keyed by reagent id. Server-only; not networked.
    /// </summary>
    public Dictionary<string, (float Modifier, TimeSpan Expiry)> Sources = new();

    /// <summary>
    ///     Multiplier captured before the first chem source, so perks can be restored
    ///     when all chem sources expire.
    /// </summary>
    public float BaseHeavyCostMultiplier = 1f;

    public bool ChemInitialized;
}
