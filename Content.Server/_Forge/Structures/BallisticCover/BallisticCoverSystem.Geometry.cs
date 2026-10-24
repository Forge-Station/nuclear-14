using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Dynamics;
using Robust.Shared.Physics.Collision.Shapes;
using System.Numerics;

namespace Content.Server._Forge.Structures.BallisticCover;

public sealed partial class BallisticCoverSystem
{
    // Local views for the engine distance API. They are not attached to entities,
    // do not modify the real fixtures, and exclude fly-by fixtures from the exact bullet check.
    // Used only for synchronous queries without raising gameplay events.
    private readonly FixturesComponent _distanceFirst = new();
    private readonly FixturesComponent _distanceSecond = new();

    private bool TryFixtureRayHit(EntityUid uid, Fixture fixture, Vector2 origin, Vector2 translation, out float fraction)
    {
        fraction = 0;
        var pose = new Robust.Shared.Physics.Transform(_transform.GetWorldPosition(uid), _transform.GetWorldRotation(uid));
        var localOrigin = Robust.Shared.Physics.Transform.InvTransformPoint(pose, origin);
        var localEnd = Robust.Shared.Physics.Transform.InvTransformPoint(pose, origin + translation);
        var delta = localEnd - localOrigin;
        if (fixture.Shape is PhysShapeAabb box)
        {
            var ray = new Ray(localOrigin, Vector2.Normalize(delta));
            if (!ray.Intersects(box.LocalBounds, out var distance, out _) || distance > translation.Length())
                return false;
            fraction = distance / translation.Length();
            return true;
        }
        if (fixture.Shape is PhysShapeCircle circle)
        {
            // Circles also participate in choosing the nearest ordinary target. The fallback
            // ray checks the center segment; CastShape still accounts for bullet width.
            var offset = localOrigin - circle.Position;
            var c = Vector2.Dot(offset, offset) - circle.Radius * circle.Radius;
            if (c <= 0)
                return true;
            var a = Vector2.Dot(delta, delta);
            var b = Vector2.Dot(offset, delta);
            var discriminant = (double) b * b - (double) a * c;
            if (b >= 0 || discriminant < 0)
                return false;
            fraction = (float) ((-b - Math.Sqrt(discriminant)) / a);
            return fraction >= 0 && fraction <= 1;
        }
        if (fixture.Shape is not PolygonShape polygon || polygon.VertexCount < 3)
            return false;

        // Clip the segment against the half-planes of a convex polygon. Public CastRay
        // uses the same GJK as CastShape for PolygonShape with Radius > 0, so an independent
        // fallback checks the polygon edges without expanding its AABB.
        double enter = 0, exit = 1;
        for (var i = 0; i < polygon.VertexCount; i++)
        {
            var normal = polygon.Normals[i];
            var numerator = (double) normal.X * (polygon.Vertices[i].X - localOrigin.X) +
                (double) normal.Y * (polygon.Vertices[i].Y - localOrigin.Y);
            var denominator = (double) normal.X * delta.X + (double) normal.Y * delta.Y;
            if (Math.Abs(denominator) < 1e-10)
            {
                if (numerator < 0) return false;
                continue;
            }
            if (denominator < 0) enter = Math.Max(enter, numerator / denominator);
            else exit = Math.Min(exit, numerator / denominator);
            if (enter > exit) return false;
        }
        fraction = (float) enter;
        return true;
    }

    private bool FixturesOverlap(EntityUid first, Fixture firstFixture, PhysicsComponent firstBody,
        Robust.Shared.Physics.Transform firstPose, EntityUid second, Fixture secondFixture,
        PhysicsComponent secondBody, Robust.Shared.Physics.Transform secondPose)
    {
        _distanceFirst.Owner = first;
        _distanceSecond.Owner = second;
        _distanceFirst.Fixtures.Add("query", firstFixture);
        _distanceSecond.Fixtures.Add("query", secondFixture);
        try
        {
            return _physics.TryGetNearest(first, second, out _, out _, out var distance,
                firstPose, secondPose, _distanceFirst, _distanceSecond, firstBody, secondBody) &&
                distance <= PhysicsConstants.LinearSlop;
        }
        finally
        {
            _distanceFirst.Fixtures.Clear();
            _distanceSecond.Fixtures.Clear();
            _distanceFirst.Owner = EntityUid.Invalid;
            _distanceSecond.Owner = EntityUid.Invalid;
        }
    }
}
