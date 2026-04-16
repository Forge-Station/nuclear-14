using Content.Shared._Forge.Weapons.Ranged.Components;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Serialization;


namespace Content.Shared._Forge.Weapons.Ranged.Systems;


public abstract class SharedGunConditionSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<GunConditionComponent, AttemptShootEvent>(OnAttemptShoot);
        SubscribeLocalEvent<GunConditionComponent, ExaminedEvent>(OnExamined);
    }

    private void OnAttemptShoot(Entity<GunConditionComponent> ent, ref AttemptShootEvent args)
    {
        if (IsBroken(ent.Comp))
        {
            args.Cancelled = true;
            args.Message = Loc.GetString("gun-condition-shot-blocked-broken");
            return;
        }

        if (!ent.Comp.Jammed)
            return;

        args.Cancelled = true;
        args.Message = Loc.GetString("gun-condition-shot-blocked-jammed");
    }

    private void OnExamined(Entity<GunConditionComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        var percent = GetConditionPercent(ent.Comp);
        var state = GetConditionState(ent.Comp);
        var stateColor = GetConditionColor(state);
        var (jamStart, jamPeak) = GetJamThresholds(ent.Comp);
        var shotsToBreak = GetEstimatedShotsToBreak(ent.Comp);

        using (args.PushGroup(nameof(GunConditionComponent)))
        {
            args.PushMarkup(
                Loc.GetString(
                    "gun-condition-examine-state",
                    ("state", Loc.GetString($"gun-condition-state-{GetConditionStateId(state)}")),
                    ("color", stateColor),
                    ("value", $"{percent:0}")));

            args.PushMarkup(
                Loc.GetString(
                    "gun-condition-examine-reserve",
                    ("color", stateColor),
                    ("current", $"{GetConditionReserve(ent.Comp):0.#}"),
                    ("max", $"{GetConditionReserveMax(ent.Comp):0.#}")));

            args.PushMarkup(
                Loc.GetString(
                    "gun-condition-examine-jam-chance",
                    ("color", stateColor),
                    ("value", $"{GetJamChance(ent.Comp) * 100f:0.#}")));

            args.PushMarkup(
                Loc.GetString(
                    "gun-condition-examine-jam-formula",
                    ("startChance", $"{Math.Clamp(ent.Comp.JamChanceMin, 0f, 1f) * 100f:0.#}"),
                    ("start", $"{jamStart:0.#}"),
                    ("peakChance", $"{Math.Clamp(ent.Comp.JamChanceMax, 0f, 1f) * 100f:0.#}"),
                    ("peak", $"{jamPeak:0.#}"),
                    ("curve", $"{GetJamCurve(ent.Comp):0.##}")));

            args.PushMarkup(
                Loc.GetString(
                    "gun-condition-examine-wear",
                    ("wear", $"{ent.Comp.WearPerShot:0.##}"),
                    ("repair", $"{ent.Comp.RepairAmount:0.##}")));

            if (shotsToBreak != null)
            {
                args.PushMarkup(
                    Loc.GetString(
                        "gun-condition-examine-shots-left",
                        ("color", stateColor),
                        ("value", shotsToBreak.Value)));
            }

            if (ent.Comp.Jammed)
                args.PushMarkup(Loc.GetString("gun-condition-examine-jammed"));
        }
    }

    protected bool IsBroken(GunConditionComponent component) => component.Condition <= component.BrokenThreshold;

    protected float GetJamChance(GunConditionComponent component)
    {
        var percent = GetConditionPercent(component);
        var (startThreshold, peakThreshold) = GetJamThresholds(component);

        // Выше порога начала клинов клин невозможен.
        if (percent > startThreshold)
            return 0f;

        var minChance = Math.Clamp(component.JamChanceMin, 0f, 1f);
        var maxChance = Math.Clamp(component.JamChanceMax, 0f, 1f);

        if (percent <= peakThreshold)
            return maxChance;

        var window = startThreshold - peakThreshold;
        if (window <= 0f)
            return maxChance;

        var progress = Math.Clamp((startThreshold - percent) / window, 0f, 1f);
        var curved = MathF.Pow(progress, GetJamCurve(component));
        var chance = minChance + (maxChance - minChance) * curved;
        return Math.Clamp(chance, 0f, 1f);
    }

    protected float GetConditionFraction(GunConditionComponent component)
    {
        if (component.MaxCondition <= 0f)
            return 0f;

        return Math.Clamp(component.Condition / component.MaxCondition, 0f, 1f);
    }

    protected float GetConditionPercent(GunConditionComponent component) => GetConditionFraction(component) * 100f;

    protected float GetConditionReserve(GunConditionComponent component)
        => Math.Max(0f, component.Condition - component.BrokenThreshold);

    protected float GetConditionReserveMax(GunConditionComponent component)
        => Math.Max(0f, component.MaxCondition - component.BrokenThreshold);

    protected int? GetEstimatedShotsToBreak(GunConditionComponent component)
    {
        if (component.WearPerShot <= 0f)
            return null;

        return (int) Math.Ceiling(GetConditionReserve(component) / component.WearPerShot);
    }

    protected (float Start, float Peak) GetJamThresholds(GunConditionComponent component)
    {
        var start = component.JamStart;
        var peak = component.JamPeak;

        if (start < peak)
            (start, peak) = (peak, start);

        return (start, peak);
    }

    protected float GetJamCurve(GunConditionComponent component)
        => Math.Max(component.JamCurve, 0.01f);

    private GunConditionState GetConditionState(GunConditionComponent component)
    {
        var percent = GetConditionPercent(component);

        if (IsBroken(component))
            return GunConditionState.Broken;

        if (percent <= component.CriticalThreshold)
            return GunConditionState.Critical;

        if (percent <= component.DamagedThreshold)
            return GunConditionState.Damaged;

        if (percent <= component.WornThreshold)
            return GunConditionState.Worn;

        return GunConditionState.Good;
    }

    private string GetConditionStateId(GunConditionState state) => state switch
    {
        GunConditionState.Good => "good",
        GunConditionState.Worn => "worn",
        GunConditionState.Damaged => "damaged",
        GunConditionState.Critical => "critical",
        _ => "broken",
    };

    private string GetConditionColor(GunConditionState state) => state switch
    {
        GunConditionState.Good => "#4CAF50",
        GunConditionState.Worn => "#EAB308",
        GunConditionState.Damaged => "#F97316",
        GunConditionState.Critical => "#EF4444",
        _ => "#B91C1C",
    };

    private enum GunConditionState
    {
        Good,
        Worn,
        Damaged,
        Critical,
        Broken,
    }
}

[Serializable, NetSerializable,]
public sealed partial class GunConditionRepairDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable,]
public sealed partial class GunConditionUnjamDoAfterEvent : SimpleDoAfterEvent;
