using System.Diagnostics.CodeAnalysis;
using Content.Shared.Foldable;
using Content.Shared.Hands.Components;
using Content.Shared.Popups;
using Content.Shared.WeaponMounts.Overheat;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;


namespace Content.Shared.WeaponMounts;


/// <summary>
///     Обрабатывает события на оружии пока оно прикреплено к станку:
///     сектор стрельбы, свободные руки, ретрансляция перегрева.
/// </summary>
public sealed class MountableWeaponSystem : EntitySystem
{
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedWeaponMountSystem _mounts = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<MountableWeaponComponent, AttemptShootEvent>(OnAttemptShoot);
        SubscribeLocalEvent<MountableWeaponComponent, MountedWeaponShootAttemptEvent>(OnMountedShootAttempt);
        SubscribeLocalEvent<MountableWeaponComponent, TakeAmmoEvent>(OnTakeAmmo);
        SubscribeLocalEvent<MountableWeaponComponent, OverheatedChangedEvent>(OnOverheated);
        SubscribeLocalEvent<MountableWeaponComponent, HeatChangedEvent>(OnHeatChanged);
    }

    // ── Обработчики ───────────────────────────────────────────────────────────

    /// <summary>
    ///     Ванильное событие: только блокируем выстрел если оружие не на станке.
    ///     Сектор огня и руки проверяются в <see cref="OnMountedShootAttempt" />.
    /// </summary>
    private void OnAttemptShoot(Entity<MountableWeaponComponent> ent, ref AttemptShootEvent args)
    {
        if (ent.Comp.RequiresMount && ent.Comp.MountedTo == null)
            args.Cancelled = true;
    }

    /// <summary>
    ///     Наше событие с координатами прицела.
    ///     Проверяет сектор огня, автоповорот и свободные руки.
    ///     Должно быть поднято до передачи управления ванильному GunSystem.
    /// </summary>
    private void OnMountedShootAttempt(Entity<MountableWeaponComponent> ent, ref MountedWeaponShootAttemptEvent args)
    {
        if (ent.Comp.MountedTo == null)
            return;

        var mountEntity = GetEntity(ent.Comp.MountedTo.Value);

        // ── Проверка сектора огня ─────────────────────────────────────────────
        var mountPos = _transform.GetWorldPosition(ent);
        var targetPos = _transform.ToWorldPosition(args.Target);
        var targetDir = Angle.FromWorldVec(targetPos - mountPos);
        var mountFront = _transform.GetWorldRotation(mountEntity);
        var deviation = Angle.ShortestDistance(mountFront, targetDir).Degrees;

        if (Math.Abs(deviation) > ent.Comp.ShootArc / 2f)
        {
            args.Cancelled = true;

            if (TryComp(mountEntity, out WeaponMountComponent? mount) && mount.CanRotateWithoutTool)
            {
                var diff = targetDir.GetCardinalDir() - mountFront.GetCardinalDir();
                if (diff > 4)
                    diff -= 8;
                if (diff < -4)
                    diff += 8;
                _mounts.RotateMount((mountEntity, mount), args.User, diff * 45);
            }

            return;
        }

        // ── Проверка свободных рук ────────────────────────────────────────────
        if (CountFreeHands(args.User) < ent.Comp.RequiredFreeHands)
        {
            args.Cancelled = true;
            _popup.PopupClient(Loc.GetString("mountable-weapon-no-free-hands"), args.User, PopupType.SmallCaution);
        }
    }

    private void OnTakeAmmo(Entity<MountableWeaponComponent> ent, ref TakeAmmoEvent args)
    {
        if (ent.Comp.MountedTo == null)
            return;

        if (!_mounts.TryGetWeaponAmmo(ent, out var count, out _))
            return;

        var layer = GetAmmoLayer(ent);
        _appearance.SetData(GetEntity(ent.Comp.MountedTo.Value), layer, count - 1 > 0);
    }

    private void OnOverheated(Entity<MountableWeaponComponent> ent, ref OverheatedChangedEvent args)
    {
        if (ent.Comp.MountedTo == null)
            return;

        var ev = new MountWeaponRelayEvent<OverheatedChangedEvent>(args);
        RaiseLocalEvent(GetEntity(ent.Comp.MountedTo.Value), ref ev);
    }

    private void OnHeatChanged(Entity<MountableWeaponComponent> ent, ref HeatChangedEvent args)
    {
        if (ent.Comp.MountedTo == null)
            return;

        var ev = new MountWeaponRelayEvent<HeatChangedEvent>(args);
        RaiseLocalEvent(GetEntity(ent.Comp.MountedTo.Value), ref ev);
    }

    // ── Публичный API ─────────────────────────────────────────────────────────

    /// <summary>Возвращает станок, к которому прикреплено оружие.</summary>
    public bool TryGetMount(
        EntityUid weapon,
        [NotNullWhen(true)] out EntityUid? mount,
        MountableWeaponComponent? comp = null
    )
    {
        mount = null;
        if (!Resolve(weapon, ref comp, false) || comp.MountedTo == null)
            return false;

        mount = GetEntity(comp.MountedTo.Value);
        return true;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    ///     Считает пустые руки. CountFreeHands отсутствует в ванильном SharedHandsSystem.
    /// </summary>
    private int CountFreeHands(EntityUid user)
    {
        if (!TryComp(user, out HandsComponent? hands))
            return 0;

        var free = 0;
        foreach (var hand in hands.Hands.Values)
            if (hand.HeldEntity == null)
                free++;
        return free;
    }

    private WeaponMountLayers GetAmmoLayer(Entity<MountableWeaponComponent> ent) =>
        TryComp(ent.Owner, out FoldableComponent? f) && f.IsFolded
            ? WeaponMountLayers.FoldedAmmo
            : WeaponMountLayers.DeployedAmmo;
}

/// <summary>Обёртка для ретрансляции событий с оружия на его станок.</summary>
[ByRefEvent]
public record struct MountWeaponRelayEvent<TEvent>(TEvent Args);
