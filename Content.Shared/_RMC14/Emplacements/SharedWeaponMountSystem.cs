using System.Diagnostics.CodeAnalysis;
using Content.Shared.ActionBlocker;
using Content.Shared.Actions;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.CombatMode;
using Content.Shared.Construction.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Destructible;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Foldable;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Item;
using Content.Shared.Popups;
using Content.Shared.Tools.Systems;
using Content.Shared.Verbs;
using Content.Shared.WeaponMounts.Overheat;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Whitelist;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;


namespace Content.Shared.WeaponMounts;


/// <summary>
///     Базовая (shared) логика турельного станка.
///     Клиентская и серверная реализации наследуются и добавляют
///     только платформо-специфичный код.
/// </summary>
public abstract class SharedWeaponMountSystem : EntitySystem
{
    private const string MagazineSlotKey = "gun_magazine";
    private const string AmmoColor = "yellow";
    private const string FireRateColor = "yellow";
    private const string ModeColor = "cyan";
    private const string TipColor = "cyan";
    [Dependency] private readonly ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedBuckleSystem _buckle = default!;
    [Dependency] private readonly CollisionWakeSystem _collisionWake = default!;
    [Dependency] private readonly SharedCombatModeSystem _combatMode = default!;
    [Dependency] private readonly IComponentFactory _componentFactory = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedWeaponControllerSystem _controller = default!;
    [Dependency] private readonly DamageableSystem _damage = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly FoldableSystem _foldable = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedItemSystem _item = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly ItemSlotsSystem _slots = default!;
    [Dependency] private readonly SharedToolSystem _tool = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;

    public override void Initialize()
    {
        // Взаимодействие
        SubscribeLocalEvent<WeaponMountComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WeaponMountComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<WeaponMountComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<WeaponMountComponent, InteractHandEvent>(
            OnInteractHand,
            [typeof(SharedBuckleSystem),]);

        // Осмотр и действия
        SubscribeLocalEvent<WeaponMountComponent, ExaminedEvent>(OnExamine);
        SubscribeLocalEvent<WeaponMountComponent, GetVerbsEvent<AlternativeVerb>>(OnAltVerb);

        // Пристёгивание
        SubscribeLocalEvent<WeaponMountComponent, StrapAttemptEvent>(OnStrapAttempt);
        SubscribeLocalEvent<WeaponMountComponent, StrappedEvent>(OnStrapped);
        SubscribeLocalEvent<WeaponMountComponent, UnstrappedEvent>(OnUnstrapped);

        // Физика
        SubscribeLocalEvent<WeaponMountComponent, FoldAttemptEvent>(OnFoldAttempt);
        SubscribeLocalEvent<WeaponMountComponent, AnchorAttemptEvent>(OnAnchorAttempt);
        SubscribeLocalEvent<WeaponMountComponent, UnanchorAttemptEvent>(OnUnanchorAttempt);
        SubscribeLocalEvent<WeaponMountComponent, BreakageEventArgs>(OnBreak);
        SubscribeLocalEvent<WeaponMountComponent, DamageModifyEvent>(OnDamageModified);

        // DoAfter: сборка / разборка
        SubscribeLocalEvent<WeaponMountComponent, AttachWeaponDoAfterEvent>(OnAttachWeapon);
        SubscribeLocalEvent<WeaponMountComponent, DetachWeaponDoAfterEvent>(OnDetachWeapon);
        SubscribeLocalEvent<WeaponMountComponent, SecureWeaponDoAfterEvent>(OnSecureWeapon);

        // DoAfter: развёртывание
        SubscribeLocalEvent<WeaponMountComponent, DeployMountDoAfterEvent>(OnDeploy);
        SubscribeLocalEvent<WeaponMountComponent, UndeployMountDoAfterEvent>(OnUndeploy);

        // Ретрансляция событий с оружия
        SubscribeLocalEvent<WeaponMountComponent, MountWeaponRelayEvent<OverheatedChangedEvent>>(OnWeaponOverheated);
        SubscribeLocalEvent<WeaponMountComponent, MountWeaponRelayEvent<HeatChangedEvent>>(OnWeaponHeatChanged);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Инициализация карты
    // ═══════════════════════════════════════════════════════════════════════════

    private void OnMapInit(Entity<WeaponMountComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.FixedWeaponPrototype == null)
            return;

        var container = _container.EnsureContainer<ContainerSlot>(ent, ent.Comp.WeaponSlotId);
        container.OccludesLight = false;

        if (container.ContainedEntities.Count > 0)
            return;

        var weapon = SpawnInContainerOrDrop(ent.Comp.FixedWeaponPrototype, ent, ent.Comp.WeaponSlotId);

        ent.Comp.MountedEntity = weapon;
        DirtyField(ent.Owner, ent.Comp, nameof(WeaponMountComponent.MountedEntity));

        if (!TryComp(weapon, out MountableWeaponComponent? mountable))
            return;

        mountable.MountedTo = GetNetEntity(ent.Owner);
        Dirty(weapon, mountable);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Взаимодействие
    // ═══════════════════════════════════════════════════════════════════════════

    protected virtual void OnInteractUsing(Entity<WeaponMountComponent> ent, ref InteractUsingEvent args)
    {
        // ── Перезарядка прикреплённого оружия ─────────────────────────────────
        if (TryReloadWeapon(ent, args.User, args.Used))
            return;

        if (ent.Comp.IsWeaponLocked)
            return;

        if (TryComp(ent, out FoldableComponent? foldable) && foldable.IsFolded)
            return;

        // ── Прикрепление оружия ───────────────────────────────────────────────
        if (HasComp<MountableWeaponComponent>(args.Used)
            && Transform(ent).Anchored
            && ent.Comp.MountedEntity == null)
        {
            TryStartAttachWeapon(ent, args.User, args.Used);
            return;
        }

        // ── Поворот ───────────────────────────────────────────────────────────
        if (_tool.HasQuality(args.Used, ent.Comp.RotationTool))
        {
            RotateMount(ent, args.User);
            return;
        }

        // ── Снятие оружия ─────────────────────────────────────────────────────
        if (_tool.HasQuality(args.Used, ent.Comp.DismantlingTool)
            && _container.TryGetContainer(ent, ent.Comp.WeaponSlotId, out var container)
            && container.ContainedEntities.Count > 0
            && !ent.Comp.IsWeaponSecured)
            TryStartDetachWeapon(ent, args.User, args.Used);
    }

    private void OnUseInHand(Entity<WeaponMountComponent> ent, ref UseInHandEvent args)
    {

        args.Handled = true;

        if (!CanDeploy(ent, args.User, out _, out _))
            return;

        _doAfter.TryStartDoAfter(
            MakeDoAfter(
                args.User,
                ent.Comp.AssembleDelay,
                new DeployMountDoAfterEvent(),
                ent,
                args.User));
    }

    private void OnInteractHand(Entity<WeaponMountComponent> ent, ref InteractHandEvent args)
    {
        // Блокируем стандартный пристёг в режиме боя, чтобы нельзя было случайно встать.
        if (_combatMode.IsInCombatMode(args.User))
            args.Handled = true;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Физика: складывание и анкорение
    // ═══════════════════════════════════════════════════════════════════════════

    private void OnFoldAttempt(Entity<WeaponMountComponent> ent, ref FoldAttemptEvent args)
    {
        // Нельзя сложить: если стоит на карте или с оружием.
        if (Transform(ent).Anchored || ent.Comp.MountedEntity != null)
            args.Cancelled = true;
    }

    private void OnAnchorAttempt(Entity<WeaponMountComponent> ent, ref AnchorAttemptEvent args)
    {
        // Нельзя закрепить в сложенном виде.
        if (TryComp(ent, out FoldableComponent? foldable) && foldable.IsFolded)
            args.Cancel();
    }

    private void OnUnanchorAttempt(Entity<WeaponMountComponent> ent, ref UnanchorAttemptEvent args)
    {
        if (TryComp(ent, out FoldableComponent? foldable) && foldable.IsFolded)
        {
            args.Cancel();
            return;
        }

        if (ent.Comp.MountedEntity == null)
            return;

        args.Cancel();

        if (!ent.Comp.IsWeaponSecured)
        {
            // Первое снятие с анкора — фиксация оружия.
            _doAfter.TryStartDoAfter(
                MakeDoAfter(
                    args.User,
                    ent.Comp.AssembleDelay,
                    new SecureWeaponDoAfterEvent(),
                    ent,
                    args.Tool));
            return;
        }

        if (foldable != null)
            TryStartUndeploy(ent, args.User, args.Tool);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Пристёгивание оператора
    // ═══════════════════════════════════════════════════════════════════════════

    private void OnStrapAttempt(Entity<WeaponMountComponent> ent, ref StrapAttemptEvent args)
    {
        if (!AllHandsFreePopup(args.Buckle, ent, args.Popup))
        {
            args.Cancelled = true;
            return;
        }

        // Только сам себя можно пристегнуть.
        if (args.User != args.Buckle)
            args.Cancelled = true;
    }

    private void OnStrapped(Entity<WeaponMountComponent> ent, ref StrappedEvent args)
    {
        ent.Comp.User = args.Buckle;

        if (ent.Comp.MountedEntity is not { } weapon)
            return;

        _controller.StartControlling(args.Buckle, weapon);
        _actions.AddAction(args.Buckle, ref ent.Comp.DismountActionEntity, ent.Comp.DismountAction, args.Buckle);
    }

    private void OnUnstrapped(Entity<WeaponMountComponent> ent, ref UnstrappedEvent args)
    {
        ent.Comp.User = null;
        RemComp<WeaponControllerComponent>(args.Buckle);
        _actions.RemoveAction(ent.Comp.DismountActionEntity);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Осмотр и вербы
    // ═══════════════════════════════════════════════════════════════════════════

    private void OnExamine(Entity<WeaponMountComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (TryComp(ent.Comp.MountedEntity, out GunComponent? gun)
            && TryGetWeaponAmmo(ent, out var ammo, out _))
        {
            var modeName = Loc.GetString($"gun-{Enum.GetName(typeof(SelectiveFire), gun.SelectedMode)}");
            args.PushMarkup(Loc.GetString("gun-magazine-examine", ("color", AmmoColor), ("count", ammo)));
            args.PushMarkup(Loc.GetString("gun-selected-mode-examine", ("color", ModeColor), ("mode", modeName)), 4);
            args.PushMarkup(
                Loc.GetString(
                    "gun-fire-rate-examine",
                    ("color", FireRateColor),
                    ("fireRate", $"{gun.FireRateModified:0.0}")),
                3);
        }

        if (ent.Comp.Broken)
            args.PushMarkup(Loc.GetString("weapon-mount-broken-examine"));

        if (ent.Comp.IsWeaponLocked)
            return;

        // Подсказки по сборке.
        using (args.PushGroup(nameof(WeaponMountComponent)))
        {
            var hint = GetAssemblyHint(ent);
            if (hint != null)
                args.PushMarkup(Loc.GetString(hint, ("color", TipColor)), 1);
        }
    }

    private void OnAltVerb(EntityUid uid, WeaponMountComponent comp, GetVerbsEvent<AlternativeVerb> args)
    {
        if (!TryComp(comp.MountedEntity, out GunComponent? gun))
            return;

        if (!args.CanAccess || !args.CanInteract || !args.CanComplexInteract || args.Hands == null)
            return;

        // Переключение режима огня.
        // CycleFireMode — публичный метод ванильного GunSystem,
        // в отличие от приватных GetNextMode/SelectFire из RMC14.
        if ((gun.AvailableModes & ~gun.SelectedMode) != 0)
        {
            args.Verbs.Add(
                new()
                {
                    Act = () =>
                    {
                        var ev = new GunCycleFireModeEvent(args.User);
                        RaiseLocalEvent(comp.MountedEntity!.Value, ref ev);
                    },
                    Text = Loc.GetString("gun-selector-verb-cycle"),
                    Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/fold.svg.192dpi.png")),
                    Priority = 3
                });
        }


        // Свернуть развёрнутый станок (только для зафиксированных).
        if (comp.IsWeaponLocked
            && TryComp(uid, out FoldableComponent? foldable)
            && !foldable.IsFolded)
        {
            args.Verbs.Add(
                new()
                {
                    Act = () => TryStartUndeploy((uid, comp), args.User),
                    Text = Loc.GetString("weapon-mount-undeploy"),
                    Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/fold.svg.192dpi.png")),
                    Priority = 3
                });
        }

        // Извлечь магазин (только складные станки).
        if (TryComp(comp.MountedEntity, out ItemSlotsComponent? itemSlots)
            && HasComp<FoldableComponent>(uid))
        {
            foreach (var slot in itemSlots.Slots.Values)
                TryAddEjectVerb(uid, comp, slot, args);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // DoAfter: сборка оружия
    // ═══════════════════════════════════════════════════════════════════════════

    private void OnAttachWeapon(Entity<WeaponMountComponent> ent, ref AttachWeaponDoAfterEvent args)
    {
        if (args.Cancelled || args.Used == null)
            return;

        if (!TryComp(args.Used, out MountableWeaponComponent? weapon))
            return;

        if (!CanAssemble(ent, args.User))
            return;

        var container = _container.EnsureContainer<ContainerSlot>(ent, ent.Comp.WeaponSlotId);
        container.OccludesLight = false;

        if (container.ContainedEntities.Count > 0 || !_container.Insert(args.Used.Value, container))
            return;

        weapon.MountedTo = GetNetEntity(ent);
        Dirty(args.Used.Value, weapon);

        ent.Comp.MountedEntity = args.Used;
        _collisionWake.SetEnabled(ent, false);
        _item.SetSize(ent, ent.Comp.MountedWeaponSize);
        DirtyField(ent.Owner, ent.Comp, nameof(WeaponMountComponent.MountedEntity));

        UpdateAppearance(ent);
        _audio.PlayPredicted(ent.Comp.AttachSound, ent, args.User);
    }

    private void OnSecureWeapon(Entity<WeaponMountComponent> ent, ref SecureWeaponDoAfterEvent args)
    {
        if (args.Cancelled)
            return;

        ent.Comp.IsWeaponSecured = true;
        _buckle.StrapSetEnabled(ent, true);

        ApplyMountedName(ent, true);
        _audio.PlayPredicted(ent.Comp.SecureSound, ent, args.User);
    }

    private void OnDetachWeapon(Entity<WeaponMountComponent> ent, ref DetachWeaponDoAfterEvent args)
    {
        if (args.Cancelled)
            return;

        if (!_container.TryGetContainer(ent, ent.Comp.WeaponSlotId, out var container))
            return;

        _container.EmptyContainer(container);

        if (TryComp(ent.Comp.MountedEntity, out MountableWeaponComponent? mountable))
        {
            mountable.MountedTo = null;
            Dirty(ent.Comp.MountedEntity.Value, mountable);
        }

        ApplyMountedName(ent, false);

        ent.Comp.MountedEntity = null;
        ent.Comp.IsWeaponSecured = false;
        _buckle.StrapSetEnabled(ent, false);
        _collisionWake.SetEnabled(ent, true);
        _item.SetSize(ent, ent.Comp.MountSize);
        DirtyField(ent.Owner, ent.Comp, nameof(WeaponMountComponent.MountedEntity));

        UpdateAppearance(ent);
        _audio.PlayPredicted(ent.Comp.DetachSound, ent, args.User);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // DoAfter: развёртывание
    // ═══════════════════════════════════════════════════════════════════════════

    private void OnDeploy(Entity<WeaponMountComponent> ent, ref DeployMountDoAfterEvent args)
    {
        if (args.Cancelled)
            return;

        if (!CanDeploy(ent, args.User, out var coords, out var rotation))
            return;

        if (TryComp(ent, out FoldableComponent? foldable))
            _foldable.SetFolded(ent, foldable, false);

        if (ent.Comp.IsWeaponLocked)
            ApplyMountedName(ent, true);

        var xform = Transform(ent);
        _transform.SetCoordinates(ent, xform, coords, rotation);
        _transform.AnchorEntity(ent, xform);
        _collisionWake.SetEnabled(ent, false);

        if (ent.Comp.MountOnDeploy && ent.Comp.MountedEntity != null)
        {
            var ammoEv = new GetAmmoCountEvent();
            RaiseLocalEvent(ent.Comp.MountedEntity.Value, ref ammoEv);

            if (ammoEv.Count > 0)
            {
                if (!AllHandsFreePopup(args.User, ent))
                    return;

                _buckle.TryBuckle(args.User, args.User, ent, popup: false);
            }
        }

        UpdateAppearance(ent);
        _audio.PlayPredicted(ent.Comp.DeploySound, ent, args.User);
    }

    private void OnUndeploy(Entity<WeaponMountComponent> ent, ref UndeployMountDoAfterEvent args)
    {
        if (args.Cancelled)
            return;

        if (!TryComp(ent, out FoldableComponent? foldable))
            return;

        UndeployMount(ent, args.User, foldable);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Повреждения
    // ═══════════════════════════════════════════════════════════════════════════

    private void OnBreak(Entity<WeaponMountComponent> ent, ref BreakageEventArgs args)
    {
        TryComp(ent, out FoldableComponent? foldable);

        ent.Comp.Broken = true;
        DirtyField(ent.Owner, ent.Comp, nameof(WeaponMountComponent.Broken));

        UndeployMount(ent, null, foldable);
        UpdateAppearance(ent);
    }

    private void OnDamageModified(Entity<WeaponMountComponent> ent, ref DamageModifyEvent args)
    {
        // В сложенном состоянии станок не получает урона.
        if (TryComp(ent, out FoldableComponent? foldable) && foldable.IsFolded)
            args.Damage = new();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Ретрансляция событий с оружия
    // ═══════════════════════════════════════════════════════════════════════════

    private void OnWeaponOverheated(
        Entity<WeaponMountComponent> ent,
        ref MountWeaponRelayEvent<OverheatedChangedEvent> relay
    )
    {
        if (relay.Args.Damage == null)
            return;

        _damage.TryChangeDamage(ent, relay.Args.Damage);

        if (ent.Comp.MountedEntity != null)
        {
            _popup.PopupClient(
                Loc.GetString("weapon-mount-overheated", ("weapon", ent.Comp.MountedEntity.Value)),
                ent,
                ent.Comp.User,
                PopupType.SmallCaution);
        }
    }

    private void OnWeaponHeatChanged(
        Entity<WeaponMountComponent> ent,
        ref MountWeaponRelayEvent<HeatChangedEvent> relay
    ) =>
        UpdateAppearance(ent);

    // ═══════════════════════════════════════════════════════════════════════════
    // Публичный API
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Сворачивает развёрнутый станок и возвращает его в руки пользователю.</summary>
    public void UndeployMount(
        Entity<WeaponMountComponent> ent,
        EntityUid? user = null,
        FoldableComponent? foldable = null
    )
    {
        if (ent.Comp.IsWeaponLocked)
            ApplyMountedName(ent, false);

        ent.Comp.IsWeaponSecured = false;
        _transform.Unanchor(ent);

        if (foldable != null)
            _foldable.SetFolded(ent, foldable, true);

        _buckle.StrapSetEnabled(ent, false);
        _collisionWake.SetEnabled(ent, true);

        if (user != null && TryComp(user, out HandsComponent? hands))
            _hands.TryPickupAnyHand(user.Value, ent, handsComp: hands);

        UpdateAppearance(ent);
        _audio.PlayPredicted(ent.Comp.UndeploySound, ent, user);
    }

    /// <summary>Поворачивает станок на <paramref name="degrees" /> градусов.</summary>
    public void RotateMount(Entity<WeaponMountComponent> ent, EntityUid? user, int degrees = 90)
    {
        _transform.SetLocalRotation(ent, _transform.GetWorldRotation(ent) + Angle.FromDegrees(degrees));
        _audio.PlayPredicted(ent.Comp.RotateSound, ent, user);
    }

    /// <summary>Проверяет, подходит ли оружие для этого станка (вайтлист).</summary>
    public bool IsViableWeapon(EntityUid weapon, EntityUid mount, WeaponMountComponent? comp = null) =>
        Resolve(mount, ref comp, false)
        && _whitelist.IsWhitelistPassOrNull(comp.MountableWhitelist, weapon);

    /// <summary>Возвращает количество и вместимость патронов в оружии на станке.</summary>
    public bool TryGetWeaponAmmo(
        EntityUid mount,
        [NotNullWhen(true)] out int? count,
        [NotNullWhen(true)] out int? capacity,
        WeaponMountComponent? comp = null
    )
    {
        count = null;
        capacity = null;

        if (!Resolve(mount, ref comp, false) || comp.MountedEntity == null)
            return false;

        if (!_slots.TryGetSlot(comp.MountedEntity.Value, MagazineSlotKey, out var slot) || slot.Item == null)
            return false;

        var ev = new GetAmmoCountEvent();
        RaiseLocalEvent(slot.Item.Value, ref ev);

        count = ev.Count;
        capacity = ev.Capacity;
        return true;
    }

    /// <summary>Обновляет визуальные данные внешнего вида станка.</summary>
    public void UpdateAppearance(EntityUid mount, WeaponMountComponent? comp = null)
    {
        if (!Resolve(mount, ref comp, false))
            return;

        if (TryComp(mount, out FoldableComponent? foldable))
        {
            _appearance.SetData(mount, WeaponMountLayers.Deployed, !foldable.IsFolded && comp.MountedEntity != null);
            _appearance.SetData(mount, WeaponMountLayers.Folded, foldable.IsFolded && comp.MountedEntity != null);
            _appearance.SetData(mount, WeaponMountLayers.Broken, comp.Broken);
        }

        if (comp.MountedEntity == null || !TryComp(comp.MountedEntity.Value, out OverheatComponent? overheat))
            return;

        var show = foldable == null || !foldable.IsFolded;
        var alpha = show ? Math.Clamp(overheat.Heat / overheat.MaxHeat, 0f, 1f) : 0f;

        _appearance.TryGetData<Color>(mount, WeaponMountLayers.Overheated, out var color);
        _appearance.SetData(mount, WeaponMountLayers.Overheated, color.WithAlpha(alpha));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Проверки при деплое / сборке
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    ///     Проверяет, можно ли развернуть станок перед пользователем.
    ///     Заполняет целевые координаты и поворот при успехе.
    /// </summary>
    private bool CanDeploy(
        Entity<WeaponMountComponent> ent,
        EntityUid user,
        out EntityCoordinates coords,
        out Angle rotation
    )
    {
        var mover = _transform.GetMoverCoordinateRotation(user, Transform(user));
        coords = mover.Coords;
        rotation = mover.worldRot.GetCardinalDir().ToAngle();

        if (ent.Comp.Broken)
        {
            _popup.PopupClient(
                Loc.GetString("weapon-mount-deploy-broken", ("mount", ent)),
                user,
                user,
                PopupType.SmallCaution);
            return false;
        }

        coords = coords.Offset(rotation.GetCardinalDir().ToVec());

        var grid = _transform.GetGrid((user, Transform(user)));
        if (!TryComp(grid, out MapGridComponent? mapGrid))
            return true;

        // Нельзя ставить на пустой тайл (стена / вакуум).
        var tile = _mapSystem.GetTileRef(grid.Value, mapGrid, coords);
        if (tile.Tile.IsEmpty)
        {
            _popup.PopupClient(
                Loc.GetString("weapon-mount-need-open-area", ("mount", ent)),
                user,
                user,
                PopupType.SmallCaution);
            return false;
        }

        // Нельзя ставить туда, где уже стоит другой станок.
        var tileIdx = _mapSystem.TileIndicesFor(grid.Value, mapGrid, coords);
        foreach (var anchored in _mapSystem.GetAnchoredEntities(grid.Value, mapGrid, tileIdx))
            if (HasComp<WeaponMountComponent>(anchored) && anchored != ent.Owner)
            {
                _popup.PopupClient(
                    Loc.GetString("weapon-mount-need-open-area", ("mount", ent)),
                    user,
                    user,
                    PopupType.SmallCaution);
                return false;
            }

        // Зона отчуждения от других станков того же прототипа.
        if (ent.Comp.MountExclusionAreaSize > 0
            && HasNearbyMountPopup((grid.Value, mapGrid), coords, ent.Owner, ent.Comp.MountExclusionAreaSize, user))
            return false;

        return true;
    }

    private bool CanAssemble(Entity<WeaponMountComponent> ent, EntityUid user)
    {
        if (ent.Comp.MountExclusionAreaSize == 0)
            return true;

        var grid = _transform.GetGrid((ent, Transform(ent)));
        if (!TryComp(grid, out MapGridComponent? mapGrid))
            return true;

        return !HasNearbyMountPopup(
            (grid.Value, mapGrid),
            _transform.GetMoverCoordinates(ent),
            ent.Owner,
            ent.Comp.MountExclusionAreaSize,
            user);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Зона отчуждения
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    ///     Проверяет наличие станков того же прототипа в радиусе <paramref name="range" />.
    ///     Показывает попап если на сервере и нашёл.
    /// </summary>
    public bool HasNearbyMountPopup(
        Entity<MapGridComponent> grid,
        EntityCoordinates coords,
        EntityUid checking,
        float range = 1.5f,
        EntityUid? user = null
    )
    {
        if (!TryComp(checking, out MetaDataComponent? meta) || meta.EntityPrototype is not { } prototype)
            return false;

        return HasNearbyMountPopup(grid, coords, prototype, range, user);
    }

    public bool HasNearbyMountPopup(
        Entity<MapGridComponent> grid,
        EntityCoordinates coords,
        EntityPrototype? prototype,
        float range = 1.5f,
        EntityUid? user = null
    )
    {
        if (!TryGetNearbyMounts(grid, coords, out var mounts, range))
            return false;

        if (prototype == null)
            return true;

        var found = false;

        if (prototype.TryGetComponent(out WeaponMountComponent? mountComp, _componentFactory))
        {
            foreach (var mount in mounts)
                if (TryComp(mount, out MetaDataComponent? m)
                    && m.EntityPrototype == prototype
                    && mountComp.MountExclusionAreaSize > 0)
                {
                    found = true;
                    break;
                }
        }

        if (found && user != null && _net.IsServer)
        {
            _popup.PopupEntity(
                Loc.GetString("weapon-mount-too-close", ("mount", mounts[0])),
                user.Value,
                user.Value,
                PopupType.SmallCaution);
        }

        return found;
    }

    private bool TryGetNearbyMounts(
        Entity<MapGridComponent> grid,
        EntityCoordinates coords,
        out List<Entity<WeaponMountComponent>> mounts,
        float range
    )
    {
        mounts = [];
        var pos = _mapSystem.LocalToTile(grid, grid, coords);
        var area = new Box2(pos.X - range + 1, pos.Y - range + 1, pos.X + range, pos.Y + range);

        foreach (var uid in _mapSystem.GetLocalAnchoredEntities(grid, grid, area))
            if (TryComp(uid, out WeaponMountComponent? mount) && mount.MountedEntity != null)
                mounts.Add((uid, mount));

        return mounts.Count > 0;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Вспомогательные методы
    // ═══════════════════════════════════════════════════════════════════════════

    private void TryStartAttachWeapon(Entity<WeaponMountComponent> ent, EntityUid user, EntityUid weapon)
    {
        if (!CanAssemble(ent, user) || !IsViableWeapon(weapon, ent))
            return;

        _doAfter.TryStartDoAfter(
            MakeDoAfter(user, ent.Comp.AssembleDelay, new AttachWeaponDoAfterEvent(), ent, weapon));
    }

    private void TryStartDetachWeapon(Entity<WeaponMountComponent> ent, EntityUid user, EntityUid? tool = null) =>
        _doAfter.TryStartDoAfter(
            MakeDoAfter(user, ent.Comp.DisassembleDelay, new DetachWeaponDoAfterEvent(), ent, tool));

    private void TryStartUndeploy(Entity<WeaponMountComponent> ent, EntityUid user, EntityUid? tool = null) =>
        _doAfter.TryStartDoAfter(
            MakeDoAfter(user, ent.Comp.DisassembleDelay, new UndeployMountDoAfterEvent(), ent, tool));

    /// <summary>Перезаряжает оружие на станке. Возвращает true если перезарядка началась.</summary>
    private bool TryReloadWeapon(Entity<WeaponMountComponent> ent, EntityUid user, EntityUid used)
    {
        if (ent.Comp.MountedEntity == null)
            return false;

        if (!_slots.TryGetSlot(ent.Comp.MountedEntity.Value, MagazineSlotKey, out var slot))
            return false;

        if (!TryComp(used, out BallisticAmmoProviderComponent? ballistic))
            return false;

        if (!TryComp(user, out HandsComponent? hands))
            return false;

        if (!_slots.CanInsert(ent.Comp.MountedEntity.Value, used, user, slot, true))
            return false;

        if (!_hands.TryDrop(user, used))
            return false;

        if (slot.Item != null)
            _hands.TryPickupAnyHand(user, slot.Item.Value, handsComp: hands);

        _slots.TryInsert(ent.Comp.MountedEntity.Value, MagazineSlotKey, used, user, null, true);

        var layer = TryComp(ent, out FoldableComponent? fc) && fc.IsFolded
            ? WeaponMountLayers.FoldedAmmo
            : WeaponMountLayers.DeployedAmmo;

        _appearance.SetData(ent, layer, ballistic.Count > 0);
        return true;
    }

    private void EjectMagazine(EntityUid weapon, ItemSlot slot, EntityUid user, EntityUid mount)
    {
        if (!_slots.TryEjectToHands(weapon, slot, user, true))
            return;

        var layer = TryComp(mount, out FoldableComponent? fc) && fc.IsFolded
            ? WeaponMountLayers.FoldedAmmo
            : WeaponMountLayers.DeployedAmmo;

        _appearance.SetData(mount, layer, false);
    }

    private void TryAddEjectVerb(
        EntityUid uid,
        WeaponMountComponent comp,
        ItemSlot slot,
        GetVerbsEvent<AlternativeVerb> args
    )
    {
        if (slot.EjectOnInteract || slot.DisableEject)
            return;

        if (!_slots.CanEject(uid, args.User, slot))
            return;

        if (!_actionBlocker.CanPickup(args.User, slot.Item!.Value))
            return;

        var label = slot.Name != string.Empty
            ? Loc.GetString(slot.Name)
            : Comp<MetaDataComponent>(slot.Item.Value).EntityName;

        var verb = new AlternativeVerb
        {
            IconEntity = GetNetEntity(slot.Item),
            Act = () => EjectMagazine(comp.MountedEntity!.Value, slot, args.User, uid),
            Priority = 3
        };

        if (slot.EjectVerbText == null)
        {
            verb.Text = label;
            verb.Category = VerbCategory.Eject;
        }
        else
            verb.Text = Loc.GetString(slot.EjectVerbText);

        args.Verbs.Add(verb);
    }

    private bool AllHandsFreePopup(EntityUid user, EntityUid mount, bool showPopup = true)
    {
        if (!TryComp(user, out HandsComponent? hands))
            return true;

        // Проверяем что все руки пусты (нет предметов ни в одной).
        // CountFreeHands и GetHandCount отсутствуют в ванильном SharedHandsSystem.
        var totalHands = hands.Hands.Count;
        var freeHands = 0;
        foreach (var hand in hands.Hands.Values)
            if (hand.HeldEntity == null)
                freeHands++;

        if (freeHands >= totalHands)
            return true;

        if (showPopup)
        {
            _popup.PopupClient(
                Loc.GetString("weapon-mount-need-hands-free"),
                mount,
                user,
                PopupType.MediumCaution);
        }

        return false;
    }

    /// <summary>Подсказка по текущему шагу сборки (null = не нужна).</summary>
    private string? GetAssemblyHint(Entity<WeaponMountComponent> ent)
    {
        var anchored = Transform(ent).Anchored;

        if (!anchored && !_foldable.IsFolded(ent))
            return "weapon-mount-hint-unanchored";

        if (ent.Comp.MountedEntity == null && anchored)
            return "weapon-mount-hint-anchored";

        if (!ent.Comp.IsWeaponSecured && ent.Comp.MountedEntity != null && !_foldable.IsFolded(ent))
            return "weapon-mount-hint-unsecured";

        if (ent.Comp.IsWeaponSecured && anchored)
            return "weapon-mount-hint-secured";

        return null;
    }

    /// <summary>Применяет название станка в зависимости от состояния (смонтировано / нет).</summary>
    private void ApplyMountedName(Entity<WeaponMountComponent> ent, bool mounted)
    {
        if (!TryComp(ent.Comp.MountedEntity, out MetaDataComponent? meta) || meta.EntityPrototype == null)
            return;

        var id = meta.EntityPrototype.ID;

        if (mounted)
        {
            _metaData.SetEntityName(ent, meta.EntityName);
            _metaData.SetEntityDescription(ent, Loc.GetString($"weapon-mount-{id}-description-mounted"));
        }
        else
        {
            _metaData.SetEntityName(ent, Loc.GetString($"weapon-mount-{id}-name"));
            _metaData.SetEntityDescription(ent, Loc.GetString($"weapon-mount-{id}-description"));
        }
    }

    private DoAfterArgs MakeDoAfter(
        EntityUid user,
        TimeSpan delay,
        DoAfterEvent ev,
        EntityUid target,
        EntityUid? used = null
    ) =>
        new(EntityManager, user, delay, ev, target, target, used)
        {
            NeedHand = true,
            BreakOnMove = true,
            BreakOnHandChange = true
        };
}
