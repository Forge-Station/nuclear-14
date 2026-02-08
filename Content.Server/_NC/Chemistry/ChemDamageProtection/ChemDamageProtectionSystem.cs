using Content.Shared.Chemistry.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.StatusEffect;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;


namespace Content.Server.Chemistry.EntitySystems;


public sealed class ChemDamageProtectionSystem : EntitySystem
{
    private const string StatusEffectKey = "chem.damage_protection";

    [Dependency] private readonly ILogManager _log = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    private readonly List<string> _removeBuffer = new(8);
    [Dependency] private readonly StatusEffectsSystem _status = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();

        _sawmill = _log.GetSawmill("chem.protection");

        SubscribeLocalEvent<ChemDamageProtectionComponent, DamageModifyEvent>(OnDamageModify);
        SubscribeLocalEvent<ChemDamageProtectionComponent, ComponentShutdown>(OnProtectionShutdown);
        SubscribeLocalEvent<ChemDamageProtectionStatusComponent, ComponentShutdown>(OnStatusShutdown);
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var query = AllEntityQuery<ChemDamageProtectionComponent>();

        while (query.MoveNext(out var uid, out var comp))
        {
            if (!comp.Dirty && now < comp.NextPruneAt)
                continue;

            SyncState(uid, comp, now);
        }
    }

    public void AddOrRefresh(
        EntityUid uid,
        string key,
        ProtoId<DamageModifierSetPrototype> modifierSetId,
        TimeSpan duration
    )
    {
        if (duration <= TimeSpan.Zero)
            return;

        var comp = EnsureComp<ChemDamageProtectionComponent>(uid);
        var now = _timing.CurTime;
        var newExpiresAt = now + duration;

        if (comp.Sources.TryGetValue(key, out var existing))
        {
            var mergedExpiresAt = existing.ExpiresAt;
            if (newExpiresAt > mergedExpiresAt)
                mergedExpiresAt = newExpiresAt;

            var setChanged = existing.ModifierSetId != modifierSetId;
            var timeChanged = mergedExpiresAt != existing.ExpiresAt;

            if (setChanged || timeChanged)
            {
                comp.Sources[key] = new(modifierSetId, mergedExpiresAt);
                comp.Dirty = true;
            }
        }
        else
        {
            comp.Sources[key] = new(modifierSetId, newExpiresAt);
            comp.Dirty = true;
        }

        if (comp.Dirty || now >= comp.NextPruneAt)
            SyncState(uid, comp, now);
    }

    private void OnDamageModify(EntityUid uid, ChemDamageProtectionComponent comp, ref DamageModifyEvent args)
    {
        var now = _timing.CurTime;

        if (comp.Dirty || now >= comp.NextPruneAt)
            SyncState(uid, comp, now);

        if (comp.CachedCombined.Coefficients.Count == 0 && comp.CachedCombined.FlatReduction.Count == 0)
            return;

        args.Damage = DamageSpecifier.ApplyModifierSet(args.Damage, comp.CachedCombined);
    }

    private void SyncState(EntityUid uid, ChemDamageProtectionComponent comp, TimeSpan now)
    {
        var pruned = PruneExpired(comp, now);

        if (comp.Sources.Count == 0)
        {
            FullCleanup(uid, comp);
            return;
        }

        if (pruned || comp.Dirty)
        {
            RefreshCombined(uid, comp);
            comp.Dirty = false;
        }

        var (minExp, maxExp) = GetExtremes(comp);
        comp.NextPruneAt = minExp;

        UpdateStatusDuration(uid, maxExp, now);
    }

    private bool PruneExpired(ChemDamageProtectionComponent comp, TimeSpan now)
    {
        if (comp.Sources.Count == 0)
            return false;

        _removeBuffer.Clear();

        foreach (var (key, src) in comp.Sources)
            if (src.ExpiresAt <= now)
                _removeBuffer.Add(key);

        if (_removeBuffer.Count == 0)
            return false;

        foreach (var key in _removeBuffer)
            comp.Sources.Remove(key);

        return true;
    }

    private void RefreshCombined(EntityUid uid, ChemDamageProtectionComponent comp)
    {
        var target = comp.CachedCombined;
        target.Coefficients.Clear();
        target.FlatReduction.Clear();

        foreach (var src in comp.Sources.Values)
        {
            if (!_proto.TryIndex(src.ModifierSetId, out var setProto))
            {
                _sawmill.Error($"Unknown ModifierSetId '{src.ModifierSetId}' on entity {uid}.");
                continue;
            }

            MergeModifiers(target, setProto);
        }
    }

    private static void MergeModifiers(DamageModifierSet target, DamageModifierSet source)
    {
        foreach (var (type, coef) in source.Coefficients)
            if (target.Coefficients.TryGetValue(type, out var current))
                target.Coefficients[type] = Math.Min(current, coef);
            else
                target.Coefficients[type] = coef;

        foreach (var (type, flat) in source.FlatReduction)
            if (target.FlatReduction.TryGetValue(type, out var current))
                target.FlatReduction[type] = current + flat;
            else
                target.FlatReduction[type] = flat;
    }

    private static (TimeSpan Min, TimeSpan Max) GetExtremes(ChemDamageProtectionComponent comp)
    {
        var min = TimeSpan.MaxValue;
        var max = TimeSpan.MinValue;

        foreach (var src in comp.Sources.Values)
        {
            var t = src.ExpiresAt;
            if (t < min)
                min = t;
            if (t > max)
                max = t;
        }

        if (min == TimeSpan.MaxValue)
            return (TimeSpan.Zero, TimeSpan.Zero);

        return (min, max);
    }

    private void UpdateStatusDuration(EntityUid uid, TimeSpan maxExp, TimeSpan now)
    {
        var timeLeft = maxExp - now;
        if (timeLeft <= TimeSpan.Zero)
        {
            _status.TryRemoveStatusEffect(uid, StatusEffectKey);
            return;
        }

        _status.TryAddStatusEffect<ChemDamageProtectionStatusComponent>(
            uid,
            StatusEffectKey,
            timeLeft,
            true);
    }

    private void FullCleanup(EntityUid uid, ChemDamageProtectionComponent comp)
    {
        _status.TryRemoveStatusEffect(uid, StatusEffectKey);
        comp.Sources.Clear();
        RemCompDeferred(uid, comp);
    }

    private void OnProtectionShutdown(EntityUid uid, ChemDamageProtectionComponent comp, ComponentShutdown args) =>
        _status.TryRemoveStatusEffect(uid, StatusEffectKey);

    private void OnStatusShutdown(EntityUid uid, ChemDamageProtectionStatusComponent status, ComponentShutdown args)
    {
        if (TryComp(uid, out ChemDamageProtectionComponent? comp))
        {
            comp.Sources.Clear();
            RemCompDeferred(uid, comp);
        }
    }
}
