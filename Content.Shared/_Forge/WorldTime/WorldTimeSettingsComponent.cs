namespace Content.Shared._Forge.WorldTime;

// Optional source selection on the main map; no map names are involved.
[RegisterComponent]
public sealed partial class WorldTimeSettingsComponent : Component
{
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public WorldTimeCycle Cycle = WorldTimeCycle.Auto;
}

public enum WorldTimeCycle : byte
{
    // Prefer an enabled LightCycle when both mechanisms are present.
    Auto,
    DayNightCycle,
    LightCycle,
}
