using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Weapons.Ranged.Components;

/// <summary>
/// Spawns a capacity of entities as ammo, alternating between two prototypes (and optionally two
/// gunshot sounds) on every shot. Used for NPCs that visually wield two different weapons but only
/// have a single simulated <see cref="GunComponent"/>.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class AlternatingEntityAmmoProviderComponent : AmmoProviderComponent
{
    /// <summary>
    /// Prototype spawned on even (first) shots.
    /// </summary>
    [DataField("protoA", required: true)]
    public EntProtoId ProtoA = default!;

    /// <summary>
    /// Prototype spawned on odd (second) shots.
    /// </summary>
    [DataField("protoB", required: true)]
    public EntProtoId ProtoB = default!;

    /// <summary>
    /// Gunshot sound used for even (first) shots. Overrides the gun's sound when set.
    /// </summary>
    [DataField("soundA")]
    public SoundSpecifier? SoundA;

    /// <summary>
    /// Gunshot sound used for odd (second) shots. Overrides the gun's sound when set.
    /// </summary>
    [DataField("soundB")]
    public SoundSpecifier? SoundB;

    /// <summary>
    /// Max capacity. If null the provider has infinite ammo.
    /// </summary>
    [DataField("capacity")]
    public int? Capacity;

    /// <summary>
    /// Actual ammo left. Initialized to capacity unless they are non-null and differ.
    /// </summary>
    [DataField("count")]
    public int? Count;

    /// <summary>
    /// Which of the two prototypes will be fired next.
    /// </summary>
    [DataField("useB"), AutoNetworkedField]
    public bool UseB;
}
