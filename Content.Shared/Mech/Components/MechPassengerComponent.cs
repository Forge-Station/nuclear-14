using Robust.Shared.Containers;
using Robust.Shared.GameStates;

namespace Content.Shared.Mech.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class MechPassengerComponent : Component
{
    [ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public EntityUid Mech;

    [ViewVariables]
    public ContainerSlot? Slot;
}
 // Forge-Change
