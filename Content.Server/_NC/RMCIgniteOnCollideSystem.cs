using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._RMC14.Fire;
using Content.Shared.Damage;
using Content.Shared.Tag;
using Content.Shared.Whitelist;
using Robust.Shared.Physics.Events;


namespace Content.Server._RMC14.Fire;


/// <summary>
///     Обрабатывает поджог и урон от тайлов с компонентом RMCIgniteOnCollideComponent.
///     Серверная система — FlammableSystem доступен только на сервере.
/// </summary>
public sealed class RMCIgniteOnCollideSystem : EntitySystem
{
    /// <summary>
    ///     Словарь: тайл -> набор сущностей, которые сейчас его касаются.
    ///     Используем вместо physics.Contacts (internal поле, недоступно извне).
    /// </summary>
    private readonly Dictionary<EntityUid, HashSet<EntityUid>> _contacts = new();

    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly FlammableSystem _flammable = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RMCIgniteOnCollideComponent, StartCollideEvent>(OnStartCollide);
        SubscribeLocalEvent<RMCIgniteOnCollideComponent, EndCollideEvent>(OnEndCollide);
        SubscribeLocalEvent<RMCIgniteOnCollideComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnStartCollide(Entity<RMCIgniteOnCollideComponent> ent, ref StartCollideEvent args)
    {
        var other = args.OtherEntity;

        // Регистрируем контакт
        if (!_contacts.TryGetValue(ent, out var set))
        {
            set = new();
            _contacts[ent] = set;
        }

        set.Add(other);

        // Поджигаем сразу при входе
        TryIgnite(ent, other);

        // Урон НЕ наносим сразу — только через Update по таймеру,
        // иначе первый тик будет двойным (здесь + сразу в Update).
    }

    private void OnEndCollide(Entity<RMCIgniteOnCollideComponent> ent, ref EndCollideEvent args)
    {
        if (_contacts.TryGetValue(ent, out var set))
            set.Remove(args.OtherEntity);
    }

    private void OnShutdown(Entity<RMCIgniteOnCollideComponent> ent, ref ComponentShutdown args) =>
        // Чистим словарь при удалении тайла чтобы не было утечки памяти
        _contacts.Remove(ent);

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<RMCIgniteOnCollideComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            // --- Повторный поджог каждые IgniteInterval секунд ---
            // Нужен чтобы сущность не успела потухнуть пока стоит на тайле
            comp.IgniteAccumulator += frameTime;
            if (comp.IgniteAccumulator >= comp.IgniteInterval)
            {
                comp.IgniteAccumulator -= comp.IgniteInterval;

                if (_contacts.TryGetValue(uid, out var igniteSet))
                {
                    foreach (var other in igniteSet)
                        TryIgnite((uid, comp), other);
                }
            }

            // --- Тик tileDamage каждые DamageInterval секунд ---
            if (comp.TileDamage == null)
                continue;

            comp.DamageAccumulator += frameTime;
            if (comp.DamageAccumulator < comp.DamageInterval)
                continue;

            comp.DamageAccumulator -= comp.DamageInterval;

            if (!_contacts.TryGetValue(uid, out var damageSet))
                continue;

            foreach (var other in damageSet)
                TryApplyTileDamage((uid, comp), other);
        }
    }


    private void TryIgnite(Entity<RMCIgniteOnCollideComponent> ent, EntityUid other)
    {
        if (!TryComp<FlammableComponent>(other, out var flammable))
            return;

        var hasBypass = HasComp<RMCFireImmunityBypassComponent>(ent);
        if (!hasBypass && _tag.HasTag(other, "FireImmune"))
            return;
        var toAdd = Math.Min(1f, ent.Comp.MaxStacks - flammable.FireStacks);
        if (toAdd > 0f)
            _flammable.AdjustFireStacks(other, toAdd, flammable);

        if (!flammable.OnFire)
            _flammable.Ignite(other, ent, flammable);
    }


    private void TryApplyTileDamage(Entity<RMCIgniteOnCollideComponent> ent, EntityUid other)
    {
        var comp = ent.Comp;

        if (comp.TileDamage == null)
            return;

        if (!TryComp<DamageableComponent>(other, out _))
            return;

        var damage = comp.TileDamage;

        if (comp.ArmorWhitelist != null &&
            comp.ArmorMultiplier != 1.0f &&
            _whitelist.IsValid(comp.ArmorWhitelist, other))
            damage = damage * comp.ArmorMultiplier;

        _damageable.TryChangeDamage(other, damage, origin: ent);
    }
}
