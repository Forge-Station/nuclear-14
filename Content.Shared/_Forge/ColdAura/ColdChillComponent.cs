using Robust.Shared.GameStates;

namespace Content.Shared._Forge.ColdAura;

[RegisterComponent]
public sealed partial class ColdChillComponent : Component
{
    [DataField("walk")]
    public float Walk = 0.6f;

    [DataField("sprint")]
    public float Sprint = 0.6f;

    [ViewVariables]
    public float TimeLeft = 0f;
}
