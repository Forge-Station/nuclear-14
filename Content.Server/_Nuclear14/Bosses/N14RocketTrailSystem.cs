using System.Numerics;
using Content.Shared._Nuclear14.Bosses;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server._Nuclear14.Bosses;

public sealed class N14RocketTrailSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<N14RocketTrailComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            comp.Accumulator += frameTime;

            if (comp.Accumulator < comp.Interval)
                continue;

            comp.Accumulator = 0f;

            var dir = _transform.GetWorldRotation(xform).ToWorldVec();
            var pos = _transform.GetMapCoordinates(uid, xform).Position;

            var perp = new Vector2(-dir.Y, dir.X);
            pos -= dir * comp.Offset + perp * _random.NextFloat(-0.12f, 0.12f);

            Spawn(comp.PuffProto, new MapCoordinates(pos, xform.MapID));
        }
    }
}