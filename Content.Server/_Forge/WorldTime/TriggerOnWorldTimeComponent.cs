namespace Content.Server._Forge.WorldTime;

// Triggers this entity at configured minutes of the round-wide day.
// Actions are supplied separately by components handling TriggerEvent.
[RegisterComponent]
public sealed partial class TriggerOnWorldTimeComponent : Component
{
    // Minutes since midnight, in the range 0..1439.
    [DataField(required: true)]
    public List<int> Minutes = new();
}
