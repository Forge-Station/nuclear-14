using System.Numerics;
using Content.Shared._Nuclear14.Bosses;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Shared.Animations;

namespace Content.Client._Nuclear14.Bosses;

public sealed class MilitaryAimIndicatorSystem : EntitySystem
{
    [Dependency] private readonly AnimationPlayerSystem _anim = default!;

    private const string ScaleAnimationKey = "n14_military_aim_scale";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MilitaryAimIndicatorComponent, ComponentInit>(OnComponentInit);
    }

    private void OnComponentInit(Entity<MilitaryAimIndicatorComponent> ent, ref ComponentInit args)
    {
        if (!TryComp(ent, out SpriteComponent? _))
            return;

        var player = EnsureComp<AnimationPlayerComponent>(ent);
        _anim.Play((ent, player), GetAnimation(ent.Comp), ScaleAnimationKey);
    }

    private Animation GetAnimation(MilitaryAimIndicatorComponent component)
    {
        var length = TimeSpan.FromSeconds(component.Duration);

        var minScale = new Vector2(MathF.Max(component.MinScale, 0.01f), MathF.Max(component.MinScale, 0.01f));

        return new Animation()
        {
            Length = length,
            AnimationTracks =
            {
                new AnimationTrackComponentProperty()
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Scale),
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(Vector2.One, 0.0f),
                        new AnimationTrackProperty.KeyFrame(minScale, length.Seconds),
                    },
                    InterpolationMode = AnimationInterpolationMode.Linear
                }
            }
        };
    }
}