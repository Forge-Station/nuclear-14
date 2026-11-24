using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared.Weapons.Ranged.Components;

/// <summary>
///     Responsible for handling recharging a basic entity ammo provider over time.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class RechargeBasicEntityAmmoComponent : Component
{
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("rechargeCooldown")]
    [AutoNetworkedField]
    public float RechargeCooldown = 1.5f;

    /// <summary>
    ///     How many rounds are restored per recharge cycle.
    ///     The last cycle is clamped to the provider's remaining capacity.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("rechargeAmount")]
    [AutoNetworkedField]
    public int RechargeAmount = 1;

    [DataField("rechargeSound")]
    [AutoNetworkedField]
    public SoundSpecifier RechargeSound = new SoundPathSpecifier("/Audio/Magic/forcewall.ogg")
    {
        Params = AudioParams.Default.WithVolume(-25f) // Lowered for N14 because no one wants to listen to dead NPCs reload.
    };

    [ViewVariables(VVAccess.ReadWrite),
     DataField("nextCharge", customTypeSerializer:typeof(TimeOffsetSerializer)),
    AutoNetworkedField]
    [AutoPausedField]
    public TimeSpan? NextCharge;
}
