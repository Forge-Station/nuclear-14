using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Shared.Buckle;
using Content.Shared.CombatMode;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;

namespace Content.Shared.WeaponMounts;

/// <summary>
///     Позволяет сущности дистанционно управлять оружием через <see cref="WeaponControllerComponent" />.
///     Компонент добавляется при пристёгивании к станку и удаляется при отстёгивании.
/// </summary>
public abstract class SharedWeaponControllerSystem : EntitySystem
{
    [Dependency] private readonly SharedBuckleSystem _buckle = default!;
    [Dependency] private readonly SharedCombatModeSystem _combatMode = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedGunSystem _gun = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WeaponControllerComponent, DismountActionEvent>(OnDismountAction);
        SubscribeLocalEvent<WeaponControllerComponent, MobStateChangedEvent>(OnMobStateChanged);

        // Клик/интеракт (может НЕ прилетать в combat mode на некоторых путях)
        SubscribeLocalEvent<WeaponControllerComponent, AfterInteractEvent>(OnAfterInteract);

        // Гарантированный путь combat-mode клика: игра пытается атаковать -> перехватываем и стреляем.
        SubscribeLocalEvent<WeaponControllerComponent, AttackAttemptEvent>(OnAttackAttempt);

        // Наш внутренний “триггер выстрела”
        SubscribeLocalEvent<WeaponControllerComponent, MountedWeaponShootAttemptEvent>(OnShootAttempt);
    }

    // ── Бакл / смерть ─────────────────────────────────────────────────────────

    private void OnDismountAction(Entity<WeaponControllerComponent> ent, ref DismountActionEvent args) =>
        _buckle.Unbuckle(ent.Owner, ent);

    private void OnMobStateChanged(Entity<WeaponControllerComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Alive)
            _buckle.Unbuckle(ent.Owner, ent);
    }
    private ISawmill _sawmill = default!;
    // ── Клик в мир (иногда работает и в combat, иногда нет — оставляем) ─────────

    private void OnAfterInteract(Entity<WeaponControllerComponent> ent, ref AfterInteractEvent args)
    {
        // В твоей ветке Cancelled у AfterInteractEvent нет — используем только Handled.
        if (args.Handled)
            return;

        var inCombat = _combatMode.IsInCombatMode(ent.Owner);
        var ctrl = ent.Comp.ControlledWeapon;

        _sawmill.Info(
            $"AfterInteract: user={ToPrettyString(ent.Owner)} combat={inCombat} hasCtrlWeapon={ctrl != null} click={args.ClickLocation}");

        if (!inCombat)
            return;

        if (ctrl == null)
        {
            _sawmill.Warning($"AfterInteract: NO ControlledWeapon on {ToPrettyString(ent.Owner)}");
            return;
        }

        var weapon = GetEntity(ctrl.Value);
        _sawmill.Info($"AfterInteract: resolved weapon={ToPrettyString(weapon)}");

        var ev = new MountedWeaponShootAttemptEvent(ent.Owner, weapon, args.ClickLocation);

        // 1) проверки на оружии
        RaiseLocalEvent(weapon, ref ev);

        _sawmill.Info($"AfterInteract: weapon-check cancelled={ev.Cancelled}");

        if (ev.Cancelled)
        {
            args.Handled = true;
            return;
        }

        // 2) передача в контроллер -> OnShootAttempt
        RaiseLocalEvent(ent.Owner, ref ev);

        _sawmill.Info($"AfterInteract: after controller raise cancelled={ev.Cancelled}");

        args.Handled = true;
    }



    // ── Combat-mode клик (попытка атаки) ───────────────────────────────────────

    private void OnAttackAttempt(Entity<WeaponControllerComponent> ent, ref AttackAttemptEvent args)
    {
        // Важно: AttackAttemptEvent летит только когда игра реально пытается атаковать.
        // Мы тут “подменяем” атаку на стрельбу с закреплённого оружия.

        if (!_combatMode.IsInCombatMode(ent.Owner))
            return;

        if (ent.Comp.ControlledWeapon == null)
            return;

        // Отменяем обычную атаку (удар рукой/предметом), иначе будут и удары и выстрелы.
        args.Cancel();

        var weapon = GetEntity(ent.Comp.ControlledWeapon.Value);

        // В AttackAttemptEvent нет координат клика.
        // Поэтому:
        // - если есть Target (клик по сущности) — целимся в неё
        // - если Target null — fallback: целимся “вперёд” (координаты самого юзера).
        // (Если тебе нужно стрелять по тайлу в combat — это делается через другой event/сетевой RequestShoot,
        //  но хотя бы стрельба по целям начнёт работать сразу.)
        var targetCoords = args.Target != null
            ? Transform(args.Target.Value).Coordinates
            : Transform(ent.Owner).Coordinates;

        var ev = new MountedWeaponShootAttemptEvent(ent.Owner, weapon, targetCoords);

        RaiseLocalEvent(weapon, ref ev);
        if (ev.Cancelled)
            return;

        RaiseLocalEvent(ent.Owner, ref ev);
    }

    // ── Реальный выстрел ──────────────────────────────────────────────────────

    private void OnShootAttempt(Entity<WeaponControllerComponent> ent, ref MountedWeaponShootAttemptEvent args)
    {
        _sawmill.Info(
            $"ShootAttempt: user={ToPrettyString(args.User)} ctrlOwner={ToPrettyString(ent.Owner)} cancelled={args.Cancelled} ctrlWeapon={ent.Comp.ControlledWeapon}");

        if (args.Cancelled || ent.Comp.ControlledWeapon == null)
            return;

        var weapon = GetEntity(ent.Comp.ControlledWeapon.Value);

        if (!TryComp(weapon, out GunComponent? gun))
        {
            _sawmill.Warning($"ShootAttempt: weapon={ToPrettyString(weapon)} has NO GunComponent");
            return;
        }

        EntityCoordinates shootFrom;
        if (_container.TryGetContainingContainer(weapon, out var container))
        {
            var mount = container.Owner;
            var rotation = _transform.GetWorldRotation(mount);
            var offset = rotation.RotateVec(new Vector2(0f, 0.5f));
            shootFrom = Transform(mount).Coordinates.Offset(offset);

            _sawmill.Info($"ShootAttempt: weapon in container mount={ToPrettyString(mount)} shootFrom={shootFrom}");
        }
        else
        {
            shootFrom = Transform(weapon).Coordinates;
            _sawmill.Info($"ShootAttempt: weapon NOT in container shootFrom={shootFrom}");
        }

        _sawmill.Info($"ShootAttempt: calling AttemptShoot target={args.Target}");
        _gun.AttemptShoot(args.User, weapon, gun, shootFrom);

        args.Cancelled = true;
        _sawmill.Info($"ShootAttempt: done -> cancelled=true");
    }


    // ── Публичный API ─────────────────────────────────────────────────────────

    public bool TryGetControlledWeapon(
        EntityUid user,
        [NotNullWhen(true)] out EntityUid? weapon,
        [NotNullWhen(true)] out GunComponent? gun)
    {
        gun = null;
        weapon = default;

        if (!TryComp(user, out WeaponControllerComponent? ctrl) || ctrl.ControlledWeapon == null)
            return false;

        weapon = GetEntity(ctrl.ControlledWeapon.Value);
        return TryComp(weapon, out gun);
    }

    public void StartControlling(EntityUid controller, EntityUid weapon)
    {
        var comp = EnsureComp<WeaponControllerComponent>(controller);
        comp.ControlledWeapon = GetNetEntity(weapon);
        Dirty(controller, comp);
    }
}
