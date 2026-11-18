namespace Content.Shared._Forge.WorldTime;

// Marks the map whose day/night phase is used as the round-wide time of day.
[RegisterComponent]
public sealed partial class WorldTimeSourceComponent : Component
{
    // Reference used by LightCycle, matching the client's round clock.
    public TimeSpan RoundStartTime;
}
