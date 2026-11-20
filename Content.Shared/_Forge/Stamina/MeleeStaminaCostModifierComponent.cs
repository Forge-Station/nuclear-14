using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Stamina;

/// <summary>
///     Multiplies the stamina cost of heavy melee attacks for this entity.
///     Values below 1 make attacks cheaper; 1 means no change.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class MeleeStaminaCostModifierComponent : Component
{
    [DataField, AutoNetworkedField]
    public float AppliedModifier = 1f;

    public Dictionary<string, (float Modifier, TimeSpan Expiry)> Sources = new();
}
