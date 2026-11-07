using Content.Server.Explosion.EntitySystems;
using Content.Shared._Forge.WorldTime;

namespace Content.Server._Forge.WorldTime;

// Converts scheduled world-time notifications into standard entity triggers.
public sealed class WorldTimeTriggerSystem : EntitySystem
{
    [Dependency] private readonly TriggerSystem _trigger = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WorldTimeReachedEvent>(OnTimeReached);
    }

    private void OnTimeReached(WorldTimeReachedEvent args)
    {
        var minute = (int) args.TimeOfDay.TotalMinutes;
        var pending = new List<EntityUid>();

        var query = EntityQueryEnumerator<TriggerOnWorldTimeComponent>();

        while (query.MoveNext(out var uid, out var schedule))
        {
            if (schedule.Minutes.Contains(minute))
                pending.Add(uid);
        }

        // Trigger handlers may delete entities or change their components.
        // Complete enumeration before invoking any of them.
        foreach (var uid in pending)
        {
            if (!Deleted(uid)
                && HasComp<TriggerOnWorldTimeComponent>(uid))
                _trigger.Trigger(uid);
        }
    }
}
