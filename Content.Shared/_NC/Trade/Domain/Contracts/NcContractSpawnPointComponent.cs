namespace Content.Shared._NC.Trade;


/// <summary>
///     World anchor for contract objective spawn/dropoff resolution.
///     Attach to map marker entities, shuttle anchors, spawners, or any other
///     map-placed entity that should be selectable by contract runtime via
///     <c>runtime.spawnPoint</c> / <c>runtime.dropoffPoint</c>.
/// </summary>
[RegisterComponent]
public sealed partial class NcContractSpawnPointComponent : Component
{
    [DataField("id")]
    public string Id { get; set; } = string.Empty;

    [DataField("groups")]
    public List<string> Groups { get; set; } = new();
}
