using Content.Shared._Forge.Warfront;
using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Warfront.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WarfrontFactionComponent : Component
{
    [DataField, AutoNetworkedField]
    public WarfrontFaction Faction;
}
