using Content.Shared.Buckle;
using Content.Shared.Rotation;
using Content.Shared.Standing;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Timing;

namespace Content.Client.Standing;

/// <inheritdoc cref="CrawlUnderComponent"/>
public sealed class CrawlUnderSystem : SharedCrawlUnderSystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IEyeManager _eyeManager = default!;
    [Dependency] private readonly StandingStateSystem _standing = default!;
    [Dependency] private readonly AnimationPlayerSystem _animation = default!;
    [Dependency] private readonly SharedBuckleSystem _buckle = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CrawlUnderComponent, MoveEvent>(OnMovementInput);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<CrawlUnderComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var crawlUnder, out var sprite))
        {
            // Не трогаем глубину отрисовки, если её поменяли со стороны.
            if (sprite.DrawDepth != crawlUnder.NormalDrawDepth
                && sprite.DrawDepth != crawlUnder.CrawlingUnderDrawDepth)
                continue;

            sprite.DrawDepth = crawlUnder.IsCrawlingUnder && _standing.IsDown(uid)
                ? crawlUnder.CrawlingUnderDrawDepth
                : crawlUnder.NormalDrawDepth;
        }

        query.Dispose();
    }

    /// <summary>
    /// Лежащая сущность разворачивается головой туда, куда ползёт.
    /// </summary>
    private void OnMovementInput(EntityUid uid, CrawlUnderComponent component, ref MoveEvent args)
    {
        if (!_timing.IsFirstTimePredicted
            || !_standing.IsDown(uid)
            || _buckle.IsBuckled(uid)
            || _animation.HasRunningAnimation(uid, "rotate")
            || !TryComp<TransformComponent>(uid, out var transform)
            || !TryComp<SpriteComponent>(uid, out var sprite)
            || !TryComp<RotationVisualsComponent>(uid, out var rotationVisuals))
            return;

        var rotation = transform.LocalRotation
                       + (_eyeManager.CurrentEye.Rotation - (transform.LocalRotation - transform.WorldRotation));

        var angle = rotation.GetDir() is Direction.SouthEast or Direction.East or Direction.NorthEast or Direction.North
            ? Angle.FromDegrees(270)
            : Angle.FromDegrees(90);

        rotationVisuals.HorizontalRotation = angle;
        sprite.Rotation = angle;
    }
}
