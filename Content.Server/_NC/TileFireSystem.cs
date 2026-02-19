using Content.Shared._RMC14.Fire;
using Content.Shared.Damage;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Events;

namespace Content.Server._RMC14.Fire;

/// <summary>
/// Управляет временем жизни тайлового огня, визуальными стаками и тушением.
/// Тушение происходит при коллизии со спреем огнетушителя (ExtinguisherSpray).
/// </summary>
public sealed class TileFireSystem : EntitySystem
{
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TileFireComponent, ComponentInit>(OnInit);

        // Ловим коллизию тайла с любой сущностью — проверяем не спрей ли это
        SubscribeLocalEvent<TileFireComponent, StartCollideEvent>(OnCollide);
    }

    private void OnInit(Entity<TileFireComponent> ent, ref ComponentInit args)
    {
        if (ent.Comp.TimeRemaining < 0f)
            ent.Comp.TimeRemaining = ent.Comp.Duration;

        // Удаляем все другие тайлы огня на этой же клетке — предотвращаем стакинг.
        // Новый тайл замещает старый (например более сильный огонь из гранаты).
        var pos = _transform.GetMapCoordinates(ent);
        foreach (var existing in _lookup.GetEntitiesInRange(pos, 0.3f))
        {
            if (existing == ent.Owner)
                continue;

            if (!HasComp<TileFireComponent>(existing))
                continue;

            QueueDel(existing);
        }

        UpdateVisuals(ent);
    }

    private void OnCollide(Entity<TileFireComponent> ent, ref StartCollideEvent args)
    {
        var other = args.OtherEntity;

        // Проверяем — это спрей огнетушителя?
        if (!HasComp<ExtinguisherSprayComponent>(other))
            return;

        // Обычный спрей — используем SprayExtinguishMultiplier
        Extinguish(ent, ent.Comp.SprayExtinguishMultiplier);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<TileFireComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            comp.TimeRemaining -= frameTime;

            if (comp.TimeRemaining <= 0f)
            {
                QueueDel(uid);
                continue;
            }

            // Обновляем визуальный стак
            var newStack = GetStack(comp);
            if (newStack != comp.CurrentStack)
            {
                comp.CurrentStack = newStack;
                UpdateVisuals((uid, comp));
            }

            // --- Урон по структурам ---
            if (comp.StructureDamage == null)
                continue;

            comp.StructureDamageAccumulator += frameTime;
            if (comp.StructureDamageAccumulator < comp.StructureDamageInterval)
                continue;

            comp.StructureDamageAccumulator -= comp.StructureDamageInterval;

            var pos = _transform.GetMapCoordinates(uid);
            foreach (var nearby in _lookup.GetEntitiesInRange(pos, 0.6f))
            {
                // Пропускаем сам тайл и мобов — они обрабатываются через коллизии
                if (nearby == uid)
                    continue;

                if (HasComp<TileFireComponent>(nearby))
                    continue;

                // Только сущности с DamageableComponent (стены, двери, машины)
                if (!TryComp<DamageableComponent>(nearby, out _))
                    continue;

                // Мобов пропускаем — их жжёт DamageOnCollideSystem через коллизии
                if (HasComp<Content.Shared.Mobs.Components.MobStateComponent>(nearby))
                    continue;

                _damageable.TryChangeDamage(nearby, comp.StructureDamage, ignoreResistances: false, origin: uid);
            }
        }
    }

    /// <summary>
    /// Тушит тайл. multiplier = 0 означает "нельзя потушить этим способом".
    /// </summary>
    public void Extinguish(EntityUid uid, float multiplier = 1f)
    {
        if (!TryComp<TileFireComponent>(uid, out var comp))
            return;

        if (comp.ExtinguishInstantly)
        {
            QueueDel(uid);
            return;
        }

        // multiplier = 0 — нельзя потушить (вечный огонь, ForeverFire)
        if (multiplier <= 0f)
            return;

        // Каждое попадание снимает 10% от полной длительности * множитель
        comp.TimeRemaining -= comp.Duration * multiplier * 0.1f;

        if (comp.TimeRemaining <= 0f)
            QueueDel(uid);
    }

    public bool CanExtinguish(EntityUid uid, bool isPat = false)
    {
        if (!TryComp<TileFireComponent>(uid, out var comp))
            return true;

        if (comp.ExtinguishInstantly)
            return true;

        var multiplier = isPat ? comp.PatExtinguishMultiplier : comp.SprayExtinguishMultiplier;
        return multiplier > 0f;
    }

    private TileFireVisualStack GetStack(TileFireComponent comp)
    {
        var ratio = comp.TimeRemaining / comp.Duration;

        return ratio switch
        {
            > 0.75f => TileFireVisualStack.Four,
            > 0.50f => TileFireVisualStack.Three,
            > 0.25f => TileFireVisualStack.Two,
            _       => TileFireVisualStack.One,
        };
    }

    private void UpdateVisuals(Entity<TileFireComponent> ent)
    {
        _appearance.SetData(ent, TileFireLayers.Base, ent.Comp.CurrentStack);
    }
}
