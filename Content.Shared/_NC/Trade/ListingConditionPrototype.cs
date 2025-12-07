using Robust.Shared.Serialization;


namespace Content.Shared._NC.Trade;


[Serializable, NetSerializable,]
public sealed class ListingConditionPrototype
{
    [DataField("condition")]
    public object? Condition;
}
