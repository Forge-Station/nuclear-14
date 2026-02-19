using Content.Shared._RMC14.Fire;
using Content.Shared.Damage;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Physics.Events;

namespace Content.Server._RMC14.Fire;

/// <summary>
/// Наносит урон сущностям стоящим на тайле с DamageOnCollideComponent.
/// </summary>
public sealed class DamageOnCollideSystem : EntitySystem
{
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;

    private readonly Dictionary<EntityUid, HashSet<EntityUid>> _contacts = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DamageOnCollideComponent, StartCollideEvent>(OnStartCollide);
        SubscribeLocalEvent<DamageOnCollideComponent, EndCollideEvent>(OnEndCollide);
        SubscribeLocalEvent<DamageOnCollideComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnStartCollide(Entity<DamageOnCollideComponent> ent, ref StartCollideEvent args)
    {
        var other = args.OtherEntity;

        if (!_contacts.TryGetValue(ent, out var set))
        {
            set = new HashSet<EntityUid>();
            _contacts[ent] = set;
        }

        // Просто регистрируем — урон нанесём через Update по таймеру.
        // НЕ вызываем TryApplyDamage здесь, иначе первый тик будет двойным.
        set.Add(other);
    }

    private void OnEndCollide(Entity<DamageOnCollideComponent> ent, ref EndCollideEvent args)
    {
        if (_contacts.TryGetValue(ent, out var set))
            set.Remove(args.OtherEntity);
    }

    private void OnShutdown(Entity<DamageOnCollideComponent> ent, ref ComponentShutdown args)
    {
        _contacts.Remove(ent);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<DamageOnCollideComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (!_contacts.TryGetValue(uid, out var set) || set.Count == 0)
                continue;

            comp.DamageAccumulator += frameTime;

            if (comp.DamageAccumulator < comp.DamageInterval)
                continue;

            comp.DamageAccumulator -= comp.DamageInterval;

            foreach (var other in set)
                TryApplyDamage((uid, comp), other);
        }
    }

    private void TryApplyDamage(Entity<DamageOnCollideComponent> ent, EntityUid other)
    {
        if (!TryComp<DamageableComponent>(other, out _))
            return;

        if (!ent.Comp.DamageDead &&
            TryComp<MobStateComponent>(other, out var mobState) &&
            _mobState.IsDead(other, mobState))
        {
            return;
        }

        _damageable.TryChangeDamage(
            other,
            ent.Comp.Damage,
            ignoreResistances: ent.Comp.IgnoreResistances,
            origin: ent);
    }
}
