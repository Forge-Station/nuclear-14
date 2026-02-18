using Content.Shared.Containers.ItemSlots;
using Content.Shared.Whitelist;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared.Weapons.Ranged.Components;

[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState(true)]
public sealed partial class ThrowingAmmoProviderComponent : Component
{
    [DataField]
    public string ContainerId = "throwing_ammo";

    [DataField, AutoNetworkedField]
    public int Capacity = 6;

    [DataField, AutoNetworkedField]
    public float DamageMultiplier = 3f;

    [DataField, AutoNetworkedField]
    public float MinimumThrowFlyTime = 0.15f;

    [DataField]
    public EntityWhitelist? Whitelist;

    [DataField]
    public SoundSpecifier? SoundInsert = new SoundPathSpecifier("/Audio/Weapons/Guns/MagIn/shotgun_insert.ogg");

    [DataField]
    public SoundSpecifier? SoundRack = new SoundPathSpecifier("/Audio/Weapons/Guns/Cock/shotgun_rack.ogg");

    [DataField, AutoNetworkedField]
    public int AmmoCount;

    [DataField]
    public ItemSlot SlotTemplate = new();
}

[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class CannonBoostedComponent : Component
{
    [DataField, AutoNetworkedField]
    public float DamageMultiplier;

    [DataField, AutoNetworkedField]
    public float MinimumFlyTime;
}


[RegisterComponent, NetworkedComponent]
public sealed partial class HiddenUntilThrownComponent : Component
{
}

public enum HlamotronVisualLayers
{
    Empty,
    Loaded
}

public enum JunkCannonInhandVisuals
{
    Loaded
}
