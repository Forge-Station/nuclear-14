using Robust.Shared.GameStates;

namespace Content.Shared._Forge.CombatModeVisuals;

[RegisterComponent, NetworkedComponent]
public sealed partial class CombatModeVisualsComponent : Component
{
    [DataField]
    public List<string> Layers = new();

    public bool LastInCombat;
}
