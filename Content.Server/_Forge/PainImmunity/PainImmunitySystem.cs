using Content.Shared.Damage;
using Content.Shared.Movement.Systems;
using Robust.Shared.Timing;

namespace Content.Server._Forge.PainImmunity;

/// <summary>
///     Applies temporary pain immunity to mobs while a source reagent is being
///     metabolized. The immunity percentage-wise reduces the movement slowdown caused
///     by damage. Multiple sources may be active at once; only the highest immunity
///     is applied, and smaller ones take over once the highest expires.
/// </summary>
public sealed partial class PainImmunitySystem : EntitySystem
{
    /// <summary>
    ///     How long a source remains valid after its last metabolism tick.
    /// </summary>
    public static readonly TimeSpan ExpiryGrace = TimeSpan.FromSeconds(10);

    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PainImmunityComponent, ModifySlowOnDamageSpeedEvent>(OnModifySpeed);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<PainImmunityComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (!Prune(comp.Sources, now))
                continue;

            var newMax = Max(comp.Sources);
            if (newMax != comp.AppliedImmunity)
            {
                comp.AppliedImmunity = newMax;
                _movement.RefreshMovementSpeedModifiers(uid);
            }

            if (comp.Sources.Count == 0)
                RemComp<PainImmunityComponent>(uid);
        }
    }

    /// <summary>
    ///     Registers (or refreshes) a source's immunity. Called once per metabolism tick.
    /// </summary>
    public void Refresh(EntityUid uid, string source, float immunity)
    {
        immunity = Math.Clamp(immunity, 0f, 1f);

        var comp = EnsureComp<PainImmunityComponent>(uid);
        var prev = comp.AppliedImmunity;

        comp.Sources[source] = (immunity, _timing.CurTime + ExpiryGrace);
        comp.AppliedImmunity = Max(comp.Sources);

        if (comp.AppliedImmunity != prev)
            _movement.RefreshMovementSpeedModifiers(uid);
    }

    /// <summary>
    ///     Reduces the damage slowdown by the given immunity fraction.
    ///     immunity=1 -> full speed (no slowdown); immunity=0.5 -> half the slowdown removed.
    /// </summary>
    private void OnModifySpeed(EntityUid uid, PainImmunityComponent comp, ref ModifySlowOnDamageSpeedEvent args)
    {
        args.Speed = 1 - (1 - args.Speed) * (1 - comp.AppliedImmunity);
    }

    private static bool Prune(Dictionary<string, (float Immunity, TimeSpan Expiry)> sources, TimeSpan now)
    {
        List<string>? expired = null;
        foreach (var (key, entry) in sources)
        {
            if (entry.Expiry >= now)
                continue;

            expired ??= new List<string>();
            expired.Add(key);
        }

        if (expired == null)
            return false;

        foreach (var key in expired)
            sources.Remove(key);

        return true;
    }

    private static float Max(Dictionary<string, (float Immunity, TimeSpan Expiry)> sources)
    {
        var max = 0f;
        foreach (var (_, entry) in sources)
            max = MathF.Max(max, entry.Immunity);

        return max;
    }
}
