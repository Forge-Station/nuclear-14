using Content.Shared.SegmentedEntity;
using Content.Shared.Weapons.Ranged.Events;

namespace Content.Server._Forge.Structures.BallisticCover;

/// <summary>
/// Captures the original list before handlers that replace it entirely.
/// A separate system allows ordering around LamiaSystem without cyclic dependencies.
/// </summary>
public sealed class BallisticCoverHitscanCaptureSystem : EntitySystem
{
    [Dependency] private readonly BallisticCoverSystem _cover = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MetaDataComponent, HitScanAfterRayCastEvent>(OnHitscan,
            before: new[] { typeof(LamiaSystem), typeof(BallisticCoverSystem) });
    }

    private void OnHitscan(Entity<MetaDataComponent> ent, ref HitScanAfterRayCastEvent args)
    {
        _cover.CaptureHitscanRay(ent.Owner, args.RayCastResults);
    }
}
