using System.Numerics;
using Content.Server.Popups;
using Content.Shared.Corvax.Forge;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Item;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;

namespace Content.Server.Corvax.Forge;

/// <summary>
///     The mannequin: mounts armor parts and top/bottom decorations,
///     then produces the finished armor when all 7 slots are filled.
/// </summary>
public sealed class N14ForgeDummySystem : EntitySystem
{
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<N14ForgeDummyComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<N14ForgeDummyComponent, InteractHandEvent>(OnInteractHand);
        SubscribeLocalEvent<N14ForgeDummyComponent, N14ForgeMiniGameCompletedMessage>(OnMiniGameCompleted);

        Subs.BuiEvents<N14ForgeDummyComponent>(N14ForgeMiniGameUiKey.Key, subs =>
        {
            subs.Event<BoundUIClosedEvent>(OnMiniGameClosed);
        });

        // Mounted parts cannot be picked off the mannequin.
        SubscribeLocalEvent<N14ForgeArmorPartComponent, GettingPickedUpAttemptEvent>(OnAttemptPickup);
        SubscribeLocalEvent<N14ForgeDecorationComponent, GettingPickedUpAttemptEvent>(OnAttemptPickup);
        // Clicking a mounted part is rerouted to the mannequin itself.
        // (InteractUsing on armor parts is handled in N14ForgeAnvilSystem.)
        SubscribeLocalEvent<N14ForgeDecorationComponent, InteractUsingEvent>(OnMountedInteractUsing);
        SubscribeLocalEvent<N14ForgeArmorPartComponent, InteractHandEvent>(OnMountedInteractHand);
        SubscribeLocalEvent<N14ForgeDecorationComponent, InteractHandEvent>(OnMountedInteractHand);
    }

    private void OnAttemptPickup(EntityUid uid, Component component, GettingPickedUpAttemptEvent args)
    {
        if (IsMountedOnDummy(uid))
            args.Cancel();
    }

    private void OnMountedInteractUsing(EntityUid uid, Component component, InteractUsingEvent args)
    {
        if (args.Handled || !IsMountedOnDummy(uid))
            return;

        // Forward to the mannequin: it decides whether to mount/claim or reject.
        var parent = Transform(uid).ParentUid;
        RaiseLocalEvent(parent, args);
        args.Handled = true;
    }

    private void OnMountedInteractHand(EntityUid uid, Component component, InteractHandEvent args)
    {
        if (args.Handled || !IsMountedOnDummy(uid))
            return;

        var parent = Transform(uid).ParentUid;
        RaiseLocalEvent(parent, args);
        args.Handled = true;
    }

    private bool IsMountedOnDummy(EntityUid uid)
    {
        return TryComp<N14ForgeDummyComponent>(Transform(uid).ParentUid, out _);
    }

    /// <summary>
    ///     Determines which armor set a part/decoration belongs to by its prototype ID:
    ///     V3 parts are Bull, V2 parts are Kratos, everything else is Monstruum (base).
    /// </summary>
    private N14ForgeArmorSet GetSet(EntityUid uid)
    {
        var protoId = MetaData(uid).EntityPrototype?.ID ?? "";
        if (protoId.Contains("V3"))
            return N14ForgeArmorSet.Bull;
        if (protoId.Contains("V2"))
            return N14ForgeArmorSet.Kratos;
        return N14ForgeArmorSet.Monstruum;
    }

    private void OnInteractUsing(EntityUid uid, N14ForgeDummyComponent component, InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        // An armor part is mounted on the mannequin.
        if (TryComp<N14ForgeArmorPartComponent>(args.Used, out var part))
        {
            if (part.Stage != N14ForgePartStage.Icon)
            {
                _popup.PopupEntity(Loc.GetString("n14-forge-dummy-not-ready"), uid, args.User);
                return;
            }

            if (!TryMountPart(uid, component, args.Used, part, args.User, out var error))
            {
                _popup.PopupEntity(error!, uid, args.User);
                return;
            }

            args.Handled = true;
            return;
        }

        // A decoration piece (top/bottom) can also be mounted.
        if (TryComp<N14ForgeDecorationComponent>(args.Used, out var decoration))
        {
            if (!TryMountDecoration(uid, component, args.Used, decoration, args.User, out var error))
            {
                _popup.PopupEntity(error!, uid, args.User);
                return;
            }

            args.Handled = true;
        }
    }

    private void OnInteractHand(EntityUid uid, N14ForgeDummyComponent component, InteractHandEvent args)
    {
        if (args.Handled)
            return;

        if (!component.IsComplete)
        {
            _popup.PopupEntity(Loc.GetString("n14-forge-dummy-incomplete"), uid, args.User);
            return;
        }

        if (!_ui.HasUi(uid, N14ForgeMiniGameUiKey.Key))
            return;

        // Opening the minigame: the player must drag all limb pieces onto the torso
        // before the finished armor is claimed.
        component.MiniGameUser = args.User;
        _ui.TryOpenUi(uid, N14ForgeMiniGameUiKey.Key, args.User);
        _ui.SetUiState(uid, N14ForgeMiniGameUiKey.Key, BuildMiniGameState(component));
        args.Handled = true;
    }

    private void OnMiniGameCompleted(EntityUid uid, N14ForgeDummyComponent component, N14ForgeMiniGameCompletedMessage args)
    {
        if (component.MiniGameUser is not { } user)
            return;

        if (component.IsComplete)
        {
            CompleteArmor(uid, component, user);
            _ui.CloseUi(uid, N14ForgeMiniGameUiKey.Key);
        }
    }

    private void OnMiniGameClosed(EntityUid uid, N14ForgeDummyComponent component, BoundUIClosedEvent args)
    {
        if (args.UiKey is not N14ForgeMiniGameUiKey.Key)
            return;

        component.MiniGameUser = null;
    }

    private N14ForgeMiniGameState BuildMiniGameState(N14ForgeDummyComponent component)
    {
        string GetProto(EntityUid? part)
            => part is { } p && Exists(p) ? MetaData(p).EntityPrototype?.ID ?? string.Empty : string.Empty;

        return new N14ForgeMiniGameState
        {
            TorsoProto = GetProto(component.Torso),
            PartProtos = new System.Collections.Generic.List<string>
            {
                GetProto(component.ArmRight),
                GetProto(component.ArmLeft),
                GetProto(component.LegRight),
                GetProto(component.LegLeft),
            }
        };
    }

    private void CompleteArmor(EntityUid uid, N14ForgeDummyComponent component, EntityUid user)
    {
        // All 7 slots filled -> claim the armor.
        var tempered = component.TemperedLock == true;
        var armorProto = component.SetLock switch
        {
            N14ForgeArmorSet.Kratos => "N14ForgeLegionArmorV2",
            N14ForgeArmorSet.Bull => "N14ForgeLegionArmorV3",
            _ => "N14ForgeLegionArmor",
        };
        if (tempered)
            armorProto += "Tempered";

        var armor = Spawn(armorProto, Transform(uid).Coordinates);
        _hands.TryPickup(user, armor, checkActionBlocker: false);

        foreach (var mounted in GetMounted(component))
        {
            if (mounted != null && Exists(mounted.Value))
                Del(mounted.Value);
        }

        component.ArmRight = null;
        component.ArmLeft = null;
        component.LegRight = null;
        component.LegLeft = null;
        component.Torso = null;
        component.DecorationTop = null;
        component.DecorationBottom = null;
        component.TemperedLock = null;
        component.SetLock = null;
        component.MiniGameUser = null;
        Dirty(uid, component);

        _popup.PopupEntity(Loc.GetString("n14-forge-dummy-claimed"), uid, user);
    }

    /// <summary>
    ///     Mounts an armor part into its dedicated slot.
    /// </summary>
    private bool TryMountPart(
        EntityUid dummy,
        N14ForgeDummyComponent component,
        EntityUid part,
        N14ForgeArmorPartComponent partComp,
        EntityUid user,
        out string? error)
    {
        error = null;

        // Mounting an armor part requires it to have leather strapped to it first.
        if (!HasComp<N14ForgeStrappedComponent>(part))
        {
            error = Loc.GetString("n14-forge-dummy-need-leather");
            return false;
        }

        // Determine the slot.
        switch (partComp.Kind)
        {
            case N14ForgePartKind.Arm when partComp.Side == N14ForgePartSide.Right:
                if (component.ArmRight != null)
                {
                    error = Loc.GetString("n14-forge-dummy-slot-taken");
                    return false;
                }
                component.ArmRight = part;
                break;
            case N14ForgePartKind.Arm:
                if (component.ArmLeft != null)
                {
                    error = Loc.GetString("n14-forge-dummy-slot-taken");
                    return false;
                }
                component.ArmLeft = part;
                break;
            case N14ForgePartKind.Leg when partComp.Side == N14ForgePartSide.Right:
                if (component.LegRight != null)
                {
                    error = Loc.GetString("n14-forge-dummy-slot-taken");
                    return false;
                }
                component.LegRight = part;
                break;
            case N14ForgePartKind.Leg:
                if (component.LegLeft != null)
                {
                    error = Loc.GetString("n14-forge-dummy-slot-taken");
                    return false;
                }
                component.LegLeft = part;
                break;
            case N14ForgePartKind.Torso:
                if (component.Torso != null)
                {
                    error = Loc.GetString("n14-forge-dummy-slot-taken");
                    return false;
                }
                component.Torso = part;
                break;
            default:
                error = Loc.GetString("n14-forge-dummy-wrong-part");
                return false;
        }

        // Tempered/normal parts cannot be mixed.
        if (component.TemperedLock is { } locked && locked != partComp.Tempered)
        {
            error = Loc.GetString("n14-forge-dummy-mix-error");
            // rollback assignment
            UndoMount(component, partComp);
            return false;
        }

        component.TemperedLock ??= partComp.Tempered;

        // Parts of different armor sets cannot be mixed.
        var partSet = GetSet(part);
        if (component.SetLock is { } setLock && setLock != partSet)
        {
            error = Loc.GetString("n14-forge-dummy-set-error");
            // rollback assignment
            UndoMount(component, partComp);
            return false;
        }

        component.SetLock ??= partSet;

        MountEntity(dummy, part);
        Dirty(dummy, component);
        return true;
    }

    private void UndoMount(N14ForgeDummyComponent component, N14ForgeArmorPartComponent partComp)
    {
        switch (partComp.Kind)
        {
            case N14ForgePartKind.Arm when partComp.Side == N14ForgePartSide.Right:
                component.ArmRight = null;
                break;
            case N14ForgePartKind.Arm:
                component.ArmLeft = null;
                break;
            case N14ForgePartKind.Leg when partComp.Side == N14ForgePartSide.Right:
                component.LegRight = null;
                break;
            case N14ForgePartKind.Leg:
                component.LegLeft = null;
                break;
            case N14ForgePartKind.Torso:
                component.Torso = null;
                break;
        }
    }

    private bool TryMountDecoration(
        EntityUid dummy,
        N14ForgeDummyComponent component,
        EntityUid decoration,
        N14ForgeDecorationComponent decorationComp,
        EntityUid user,
        out string? error)
    {
        error = null;

        // Decorations of a different armor set cannot be mixed either.
        var decoSet = GetSet(decoration);
        if (component.SetLock is { } setLock && setLock != decoSet)
        {
            error = Loc.GetString("n14-forge-dummy-set-error");
            return false;
        }

        component.SetLock ??= decoSet;

        // Bottom/top sprawls use their ondummy RSI state, which already has the
// correct baked-in placement on the mannequin.
        if (decorationComp.Top)
        {
            if (component.DecorationTop != null)
            {
                error = Loc.GetString("n14-forge-dummy-slot-taken");
                return false;
            }

            component.DecorationTop = decoration;
        }
        else
        {
            if (component.DecorationBottom != null)
            {
                error = Loc.GetString("n14-forge-dummy-slot-taken");
                return false;
            }

            component.DecorationBottom = decoration;
        }

        MountEntity(dummy, decoration);
        Dirty(dummy, component);
        return true;
    }

    /// <summary>
    ///     Reparents the part onto the mannequin with the ondummy stage.
    /// </summary>
    private void MountEntity(EntityUid dummy, EntityUid part)
    {
        _transform.SetParent(part, dummy);
        _transform.SetLocalPosition(part, Vector2.Zero);
        // Always mount upright in the world: cancel out the mannequin's own rotation
        // (SS14 rotates placed structures to the placement direction, which would flip
        // the sprites sideways).
        var dummyRotation = Transform(dummy).WorldRotation;
        _transform.SetLocalRotation(Transform(part), Angle.Zero - dummyRotation);

        if (TryComp<N14ForgeArmorPartComponent>(part, out var partComp))
        {
            partComp.Stage = N14ForgePartStage.OnDummy;
            Dirty(part, partComp);
            _appearance.SetData(part, N14ForgePartVisuals.Stage, N14ForgePartStage.OnDummy);
        }
    }

    private IEnumerable<EntityUid?> GetMounted(N14ForgeDummyComponent component)
    {
        yield return component.ArmRight;
        yield return component.ArmLeft;
        yield return component.LegRight;
        yield return component.LegLeft;
        yield return component.Torso;
        yield return component.DecorationTop;
        yield return component.DecorationBottom;
    }
}