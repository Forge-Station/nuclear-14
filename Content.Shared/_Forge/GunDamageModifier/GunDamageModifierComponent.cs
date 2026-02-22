using Content.Shared.Damage;
using Robust.Shared.GameStates;

namespace Content.Shared.Projectiles;


[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class GunDamageModifierComponent : Component
{

    [DataField, ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public float Multiplier = 1.0f;

    [DataField, ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public DamageSpecifier? FlatBonus = null;
}
