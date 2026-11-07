// Forge-Change: configure proximity-based sleep for map NPCs.
namespace Content.Shared._Forge.NPC;

/// <summary>
/// Sleeps a map NPC while no player is nearby.
/// </summary>
[RegisterComponent]
public sealed partial class ProximityNPCComponent : Component
{
    [DataField]
    public float WakeRange = 30f;

    [DataField]
    public float SleepRange = 45f;

    [DataField]
    public bool StartAsleep = true;
}
