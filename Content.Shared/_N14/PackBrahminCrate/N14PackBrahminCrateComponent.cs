using Robust.Shared.GameStates;

namespace Content.Shared._N14.PackBrahminCrate;

/// <summary>
/// Marks a brahmin mob as a pack brahmin crate.
/// First click — starts following the clicker.
/// Second click by the same player — stops and holds position.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class N14PackBrahminCrateComponent : Component
{
    [AutoNetworkedField, DataField]
    public bool IsFollowing;

    [AutoNetworkedField, DataField]
    public EntityUid? FollowTarget;
}
