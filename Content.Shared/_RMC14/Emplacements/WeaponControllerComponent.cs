using Robust.Shared.GameStates;

namespace Content.Shared.WeaponMounts;

/// <summary>
///     Даёт сущности возможность дистанционно управлять оружием.
///     Добавляется автоматически при пристёгивании к станку,
///     удаляется при отстёгивании.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedWeaponControllerSystem))]
public sealed partial class WeaponControllerComponent : Component
{
    /// <summary>Оружие, которое стреляет туда, куда целится владелец компонента.</summary>
    [DataField, AutoNetworkedField]
    public NetEntity? ControlledWeapon;
}
