using System.Numerics;

namespace Content.Shared.Throwing;

[ByRefEvent]
public struct BeforeThrowEvent
{
    public BeforeThrowEvent(EntityUid itemUid, Vector2 direction, float throwSpeed,  EntityUid playerUid)
    {
        ItemUid = itemUid;
        Direction = direction;
        ThrowSpeed = throwSpeed;
        PlayerUid = playerUid;
    }

    public EntityUid ItemUid { get; set; }
    public Vector2 Direction { get; }
    public float ThrowSpeed { get; set;}
    public EntityUid PlayerUid { get; }

    public bool Cancelled = false;
}

/// <summary>
/// Forge-Change: то же, что <see cref="BeforeThrowEvent"/>, но направленное
/// на сам бросаемый предмет - так предмет может отказаться быть брошенным.
/// </summary>
[ByRefEvent]
public struct BeforeGettingThrownEvent
{
    public BeforeGettingThrownEvent(EntityUid itemUid, Vector2 direction, float throwSpeed, EntityUid playerUid)
    {
        ItemUid = itemUid;
        Direction = direction;
        ThrowSpeed = throwSpeed;
        PlayerUid = playerUid;
    }

    public EntityUid ItemUid { get; set; }
    public Vector2 Direction { get; }
    public float ThrowSpeed { get; set; }
    public EntityUid PlayerUid { get; }

    public bool Cancelled = false;
}
