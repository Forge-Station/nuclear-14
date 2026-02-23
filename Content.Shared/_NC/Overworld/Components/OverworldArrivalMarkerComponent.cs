namespace Content.Shared.Overworld.Components;

[RegisterComponent]
public sealed partial class OverworldArrivalMarkerComponent : Component
{
    [DataField("markerID", required: true)]
    public string MarkerID = string.Empty;
}
