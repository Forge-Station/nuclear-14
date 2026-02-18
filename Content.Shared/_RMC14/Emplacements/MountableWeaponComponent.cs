using Robust.Shared.GameStates;

namespace Content.Shared.WeaponMounts;

/// <summary>
///     Помечает оружие как монтируемое — его можно закрепить на <see cref="WeaponMountComponent" />.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(MountableWeaponSystem), typeof(SharedWeaponMountSystem))]
public sealed partial class MountableWeaponComponent : Component
{
    /// <summary>Станок, к которому прикреплено оружие (null = не закреплено).</summary>
    [DataField, AutoNetworkedField]
    public NetEntity? MountedTo;

    /// <summary>Количество свободных рук, необходимых для стрельбы с монтирования.</summary>
    [DataField, AutoNetworkedField]
    public int RequiredFreeHands = 2;

    /// <summary>Оружие нельзя стрелять вне станка.</summary>
    [DataField, AutoNetworkedField]
    public bool RequiresMount = true;

    /// <summary>
    ///     Угол стрельбы (в градусах).
    ///     Выстрел за пределами этого сектора блокируется.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int ShootArc = 160;
}
