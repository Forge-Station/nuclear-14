using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;

namespace Content.Shared._Nuclear14.Bosses;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class MilitaryAimIndicatorComponent : Component
{
    [DataField, AutoNetworkedField]
    public float Duration = 5f;

    [DataField, AutoNetworkedField]
    public float MinScale = 0.05f;
}