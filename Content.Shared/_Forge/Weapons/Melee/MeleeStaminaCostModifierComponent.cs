using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Weapons.Melee;

/// <summary>
///     Multiplies the stamina cost of heavy melee attacks made by this entity.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class MeleeStaminaCostModifierComponent : Component
{
    [DataField, AutoNetworkedField]
    public float HeavyCostMultiplier = 1f;
}
