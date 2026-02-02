using Content.Shared.Actions;
using Content.Shared.Chemistry.Components;
using Content.Shared.Overlays.Switchable;
using Robust.Shared.Timing;

namespace Content.Shared.Chemistry;

public sealed class MetabolismNightVisionSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;

        var query = EntityQueryEnumerator<NocturineNightVisionMetabolismComponent>();
        while (query.MoveNext(out var uid, out var meta))
        {
            if (now < meta.ExpiresAt)
                continue;

            if (TryComp(uid, out NightVisionComponent? nightVision) && !nightVision.IsEquipment)
            {
                if (nightVision.ToggleActionEntity != null)
                {
                    _actions.RemoveAction(uid, nightVision.ToggleActionEntity);
                    nightVision.ToggleActionEntity = null;
                }

                if (meta.AddedNightVision)
                {
                    RemComp<NightVisionComponent>(uid);
                }
                else if (meta.SavedOriginal)
                {
                    var dirty = false;

                    if (nightVision.IsActive != meta.OriginalIsActive)
                    {
                        nightVision.IsActive = meta.OriginalIsActive;
                        dirty = true;
                    }

                    if (nightVision.Color != meta.OriginalColor)
                    {
                        nightVision.Color = meta.OriginalColor;
                        dirty = true;
                    }

                    if (dirty)
                        Dirty(uid, nightVision);
                }
            }

            RemComp<NocturineNightVisionMetabolismComponent>(uid);
        }
    }
}
