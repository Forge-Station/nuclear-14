using Content.Shared.Damage;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared.WeaponMounts.Overheat;

public sealed class OverheatSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly IGameTiming _time = default!;
    [Dependency] private readonly INetManager _net = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<OverheatComponent, GunShotEvent>(OnGunShot);
        SubscribeLocalEvent<OverheatComponent, AttemptShootEvent>(OnAttemptShoot);
        SubscribeLocalEvent<OverheatComponent, TryGainHeatEvent>(OnTryGainHeat);
        SubscribeLocalEvent<OverheatComponent, OverheatedChangedEvent>(OnOverheatedChanged);
    }

    // ── Handlers ─────────────────────────────────────────────────────────────

    private void OnAttemptShoot(Entity<OverheatComponent> ent, ref AttemptShootEvent args)
    {
        if (ent.Comp.Overheated)
            args.Cancelled = true;
    }

    private void OnGunShot(Entity<OverheatComponent> ent, ref GunShotEvent args)
    {
        // heat per shot may be modified by other systems through TryGainHeatEvent
        var ev = new TryGainHeatEvent(ent.Comp.HeatPerShot);
        RaiseLocalEvent(ent, ref ev);
    }

    private void OnTryGainHeat(Entity<OverheatComponent> ent, ref TryGainHeatEvent args)
    {
        var comp = ent.Comp;

        if (args.Amount == 0f)
            return;

        var oldHeat = comp.Heat;
        var newHeat = oldHeat + args.Amount;

        if (newHeat < 0f)
            newHeat = 0f;

        // If nothing changed - do nothing (prevents pointless spam)
        if (MathF.Abs(newHeat - oldHeat) < 0.0001f)
            return;

        comp.Heat = newHeat;
        Dirty(ent);

        var heatChanged = new HeatChangedEvent(comp.Heat);
        RaiseLocalEvent(ent, ref heatChanged);

        // If already overheated, we don't re-trigger "overheated reached" logic
        if (comp.Overheated)
            return;

        if (comp.Heat < comp.MaxHeat)
            return;

        // Overheat reached.
        var overheated = new OverheatedChangedEvent(true, comp.Damage);
        RaiseLocalEvent(ent, ref overheated);
    }

    private void OnOverheatedChanged(Entity<OverheatComponent> ent, ref OverheatedChangedEvent args)
    {
        var comp = ent.Comp;

        if (args.Overheated)
        {
            if (comp.Overheated)
                return; // already overheated, don't re-run

            comp.Overheated = true;
            comp.OverheatedAt = _time.CurTime;

            if (_net.IsServer)
                _audio.PlayPvs(comp.OverheatSound, ent);

            Dirty(ent);
            return;
        }

        // Recovery
        if (!comp.Overheated)
            return;

        comp.Overheated = false;

        // Emergency heat dump:
        // we want to reduce current heat by some fraction of it.
        // Example: multiplier=0.5 => remove 50% of current heat.
        // Clamp multiplier to sane range to avoid accidental heat gain.
        var mult = comp.EmergencyCooldownMultiplier;
        if (mult < 0f)
            mult = 0f;

        // Remove (Heat * mult)
        var dumpAmount = -comp.Heat * mult;
        if (dumpAmount != 0f)
        {
            var dump = new TryGainHeatEvent(dumpAmount);
            RaiseLocalEvent(ent, ref dump);
        }

        Dirty(ent);
    }

    // ── Update ───────────────────────────────────────────────────────────────

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<OverheatComponent>();

        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Heat <= 0f)
                continue;

            if (!comp.Overheated)
            {
                // Passive cooling
                var ev = new TryGainHeatEvent(-(comp.CooldownRate * frameTime));
                RaiseLocalEvent(uid, ref ev);
                continue;
            }

            // Overheated recovery after delay
            if (_time.CurTime > comp.OverheatedAt + comp.EmergencyCooldownDelay)
            {
                var ev = new OverheatedChangedEvent(false, null);
                RaiseLocalEvent(uid, ref ev);
            }
        }
    }
}

// ── Events ───────────────────────────────────────────────────────────────────

/// <summary>Attempt to change weapon heat amount.</summary>
[ByRefEvent]
public record struct TryGainHeatEvent(float Amount);

/// <summary>Heat changed (informational event).</summary>
[ByRefEvent]
public record struct HeatChangedEvent(float CurrentHeat);

/// <summary>
/// Overheated state changed.
/// </summary>
/// <param name="Overheated">true = just overheated; false = recovered.</param>
/// <param name="Damage">Damage on overheat (null on recovery).</param>
[ByRefEvent]
public record struct OverheatedChangedEvent(bool Overheated, DamageSpecifier? Damage = null);
