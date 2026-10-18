using Content.Shared.DoAfter;
using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Weapons.Ranged.Components;

/// <summary>Ограничивает скорость персонажа на время устранения клина.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class GunUnjammingComponent : Component
{
    public DoAfterId? DoAfter;
}
