namespace Content.Shared._Forge.WorldTime;

// Raised on the server when the round-wide time of day reaches a new minute.
// Subscribers can filter the event by TimeOfDay to implement
// effects such as a bell ringing at noon.
public sealed class WorldTimeReachedEvent(TimeSpan timeOfDay) : EntityEventArgs
{
    public TimeSpan TimeOfDay { get; } = timeOfDay;
}
