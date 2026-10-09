using Content.Server.Popups;
using Content.Shared.Corvax.Forge;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Prototypes;
using Content.Shared.Stacks;
using Content.Shared.Tag;
using Content.Shared.Tools.Components;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.Corvax.Forge;

/// <summary>
///     Handles everything done on the anvil:
///     hammer from dirty bloom progress (5-20 per hit), sledgehammer forging of parts from plates.
/// </summary>
public sealed class N14ForgeAnvilSystem : EntitySystem
{
    private const string LeatherProtoId = "N14MaterialLeather1";
    private const string BlacksmithApronProto = "ClothingOuterApronChemist";
    private const string BlacksmithGlovesProto = "ClothingHandsGlovesChemist";
    private const string OuterClothingSlot = "outerClothing";
    private const string GlovesSlot = "gloves";

    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly ItemToggleSystem _toggle = default!;
    [Dependency] private readonly SharedStackSystem _stack = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<N14ForgeBloomComponent, InteractUsingEvent>(OnHammerBloom);
        SubscribeLocalEvent<N14ForgePlateComponent, InteractUsingEvent>(OnForgePlate);
        SubscribeLocalEvent<N14ForgeArmorPartComponent, InteractUsingEvent>(OnForgeArmorPart);
        SubscribeLocalEvent<N14ForgeArmorPartComponent, N14ForgeStrapLeatherDoAfterEvent>(OnStrapLeather);
    }

    private void OnHammerBloom(EntityUid uid, N14ForgeBloomComponent component, InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        // Only a hammer with the forge hammer tag can be used.
        if (!_tag.HasTag(args.Used, "N14ForgeHammer"))
        {
            _popup.PopupEntity(Loc.GetString("n14-forge-not-a-hammer"), uid, args.User);
            return;
        }

        // A smith's apron and gloves are required to strike with the hammer.
        if (!RequireForgeGear(args.User, uid))
            return;

        // Need an anvil nearby.
        if (!TryGetNearbyAnvil(uid, args.User, out var anvil))
            return;

        // Already a clean bloom, nothing to do.
        if (component.Clean)
        {
            _popup.PopupEntity(Loc.GetString("n14-forge-bloom-clean"), uid, args.User);
            return;
        }

        var progressComp = EnsureComp<N14ForgeBloomProgressComponent>(uid);
        progressComp.Progress += _random.Next(5, 21);
        Dirty(uid, progressComp);

        // Forge sparks + sound (on the bloom, not the hammer).
        Spawn("N14ForgeSparks", Transform(uid).Coordinates);
        _audio.PlayPvs(new SoundPathSpecifier("/Audio/Corvax/Effects/anvil_hit.ogg"), uid);

        if (progressComp.Progress >= 100f)
        {
            // Forged into a clean bloom. Replace the dirty entity with a clean one
            // of the same armor set.
            var coords = Transform(uid).Coordinates;
            var cleanProto = component.Set switch
            {
                N14ForgeArmorSet.Kratos => "N14ForgeBloomV2",
                N14ForgeArmorSet.Bull => "N14ForgeBloomV3",
                _ => "N14ForgeBloom",
            };
            var cleanBloom = Spawn(cleanProto, coords);
            _popup.PopupEntity(Loc.GetString("n14-forge-bloom-clean"), cleanBloom, args.User);
            Del(uid);
            args.Handled = true;
            return;
        }

        _popup.PopupEntity(Loc.GetString("n14-forge-bloom-progress", ("progress", (int)progressComp.Progress)), uid, args.User);
        args.Handled = true;
    }

    /// <summary>
    ///     Sledgehammer hits on plates (arm/leg/torso plates). The first hit turns the plate
    ///     into an armor part being forged on the anvil (stage OnAnvil).
    /// </summary>
    private void OnForgePlate(EntityUid uid, N14ForgePlateComponent component, InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (_tag.HasTag(args.Used, "N14ForgeSledgehammer"))
        {
            ForgePlate(uid, component, args);
            return;
        }

        // Not a sledgehammer - maybe a welder welding 4 flat plates (тарелки) into a torso plate.
        if (component.PlateType == N14ForgePlateType.Plate && TryComp<WelderComponent>(args.Used, out var welder))
        {
            WeldPlates(uid, component, args);
            return;
        }

        _popup.PopupEntity(Loc.GetString("n14-forge-not-a-sledgehammer"), uid, args.User);
    }

    private void ForgePlate(EntityUid uid, N14ForgePlateComponent component, InteractUsingEvent args)
    {
        // A smith's apron and gloves are required to strike with the sledgehammer.
        if (!RequireForgeGear(args.User, uid))
            return;

        if (!TryGetNearbyAnvil(uid, args.User, out var anvil))
            return;

        // Only forged (кованая) and tempered (закалённая) plates can be forged on the anvil.
        if (component.Variant is not (N14ForgePlateVariant.Forged or N14ForgePlateVariant.Tempered))
        {
            _popup.PopupEntity(Loc.GetString("n14-forge-plate-invalid"), uid, args.User);
            return;
        }

        var tempered = component.Variant == N14ForgePlateVariant.Tempered;
        var part = CreateArmorPart(uid, component.PlateType, component.Set, tempered, anvil);
        if (part == null)
        {
            _popup.PopupEntity(Loc.GetString("n14-forge-plate-invalid"), uid, args.User);
            return;
        }

        var coords = Transform(uid).Coordinates;
        var newPart = Spawn(part.Value, coords);
        _appearance.SetData(newPart, N14ForgePartVisuals.Stage, N14ForgePartStage.OnAnvil);
        if (TryComp<N14ForgeArmorPartComponent>(newPart, out var partComp))
            partComp.Stage = N14ForgePartStage.OnAnvil;

        // Forge sparks + sound. Played on the new part instead of the deleted plate,
        // otherwise the sound is cut off the moment the plate is removed.
        Spawn("N14ForgeSparks", coords);
        _audio.PlayPvs(new SoundPathSpecifier("/Audio/Corvax/Effects/anvil_hit.ogg"), newPart);

        Del(uid);
        args.Handled = true;
    }

    private void WeldPlates(EntityUid uid, N14ForgePlateComponent component, InteractUsingEvent args)
    {
        if (!_toggle.IsActivated(args.Used))
        {
            _popup.PopupEntity(Loc.GetString("n14-forge-weld-welder-off"), uid, args.User);
            return;
        }

        if (!TryComp<StackComponent>(uid, out var stack) || stack.Count < 4)
        {
            _popup.PopupEntity(Loc.GetString("n14-forge-weld-not-enough"), uid, args.User);
            return;
        }

        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, 6f, new N14ForgeWeldDoAfterEvent(), uid, target: uid, used: args.Used)
        {
            BreakOnDamage = true,
            BreakOnMove = true,
            NeedHand = true,
        });
        args.Handled = true;
    }

    /// <summary>
    ///     Further sledgehammer hits on already-forged armor parts:
    ///     OnAnvil -> OnAnvil2 -> Icon (finished).
    /// </summary>
    private void OnForgeArmorPart(EntityUid uid, N14ForgeArmorPartComponent component, InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        // A part already mounted on the mannequin: forward the use to the mannequin itself.
        if (component.Stage == N14ForgePartStage.OnDummy)
        {
            var parent = Transform(uid).ParentUid;
            if (TryComp<N14ForgeDummyComponent>(parent, out _))
            {
                RaiseLocalEvent(parent, args);
                args.Handled = true;
                return;
            }
        }

        // Leather clicked on the part starts a DoAfter; after it finishes the strap is applied.
        if (MetaData(args.Used).EntityPrototype?.ID == LeatherProtoId)
        {
            if (component.Stage != N14ForgePartStage.Icon)
            {
                _popup.PopupEntity(Loc.GetString("n14-forge-dummy-not-ready"), uid, args.User);
                return;
            }

            if (HasComp<N14ForgeStrappedComponent>(uid))
            {
                _popup.PopupEntity(Loc.GetString("n14-forge-part-already-strapped"), uid, args.User);
                return;
            }

            _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, 2f, new N14ForgeStrapLeatherDoAfterEvent(), uid, target: uid, used: args.Used)
            {
                BreakOnMove = true,
                BreakOnDamage = true,
                NeedHand = true
            });
            _popup.PopupEntity(Loc.GetString("n14-forge-part-strapping"), uid, args.User);
            args.Handled = true;
            return;
        }

        if (!_tag.HasTag(args.Used, "N14ForgeSledgehammer"))
        {
            _popup.PopupEntity(Loc.GetString("n14-forge-not-a-sledgehammer"), uid, args.User);
            return;
        }

        // A smith's apron and gloves are required to strike with the sledgehammer.
        if (!RequireForgeGear(args.User, uid))
            return;

        if (!TryGetNearbyAnvil(uid, args.User, out _))
            return;

        if (component.Stage == N14ForgePartStage.Icon)
        {
            _popup.PopupEntity(Loc.GetString("n14-forge-part-already-finished"), uid, args.User);
            return;
        }

        var progressComp = EnsureComp<N14ForgePartProgressComponent>(uid);
        progressComp.Progress += _random.Next(3, 7);
        Dirty(uid, progressComp);

        var progress = progressComp.Progress;
        if (progress >= 100f)
        {
            component.Stage = N14ForgePartStage.Icon;
            _popup.PopupEntity(Loc.GetString("n14-forge-part-progress", ("progress", 100)), uid, args.User);
            _popup.PopupEntity(Loc.GetString("n14-forge-part-finished"), uid, args.User);
        }
        else if (progress >= 50f)
        {
            component.Stage = N14ForgePartStage.OnAnvil2;
            _popup.PopupEntity(Loc.GetString("n14-forge-part-progress", ("progress", (int)progress)), uid, args.User);
        }
        else
        {
            component.Stage = N14ForgePartStage.OnAnvil;
            _popup.PopupEntity(Loc.GetString("n14-forge-part-progress", ("progress", (int)progress)), uid, args.User);
        }

        Dirty(uid, component);
        _appearance.SetData(uid, N14ForgePartVisuals.Stage, component.Stage);

        // Forge sparks + sound.
        Spawn("N14ForgeSparks", Transform(uid).Coordinates);
        _audio.PlayPvs(new SoundPathSpecifier("/Audio/Corvax/Effects/anvil_hit.ogg"), uid);
        args.Handled = true;
    }

    private void OnStrapLeather(EntityUid uid, N14ForgeArmorPartComponent component, N14ForgeStrapLeatherDoAfterEvent args)
    {
        if (args.Cancelled)
            return;

        // The part must still be a finished, unstrapped piece.
        if (component.Stage != N14ForgePartStage.Icon || HasComp<N14ForgeStrappedComponent>(uid))
            return;

        if (args.Used is not { } leather || !Exists(leather))
            return;

        EnsureComp<N14ForgeStrappedComponent>(uid);
        _stack.Use(leather, 1);
        _popup.PopupEntity(Loc.GetString("n14-forge-part-strapped"), uid, args.User);
    }

    /// <summary>
    ///     Turns a plate into the appropriate armor part prototype.
    ///     Arms/legs alternate right/left based on anvil counters,
    ///     torso plates become torso parts directly.
    /// </summary>
    private ProtoId<EntityPrototype>? CreateArmorPart(EntityUid plate, N14ForgePlateType plateType, N14ForgeArmorSet set, bool tempered, Entity<N14ForgeAnvilComponent> anvil)
    {
        // V2 is Kratos, V3 is Bull, the empty base is Monstruum.
        var setSuffix = set switch
        {
            N14ForgeArmorSet.Kratos => "V2",
            N14ForgeArmorSet.Bull => "V3",
            _ => "",
        };
        var temperedSuffix = tempered ? "Tempered" : "";

        switch (plateType)
        {
            case N14ForgePlateType.ArmPlate:
            {
                var isRight = anvil.Comp.ArmsForged % 2 == 0;
                anvil.Comp.ArmsForged++;
                Dirty(anvil);
                var side = isRight ? "Right" : "Left";
                return $"N14ForgeArmorArm{side}{temperedSuffix}{setSuffix}";
            }
            case N14ForgePlateType.LegPlate:
            {
                var isRight = anvil.Comp.LegsForged % 2 == 0;
                anvil.Comp.LegsForged++;
                Dirty(anvil);
                var side = isRight ? "Right" : "Left";
                return $"N14ForgeArmorLeg{side}{temperedSuffix}{setSuffix}";
            }
            case N14ForgePlateType.TorsoPlate:
                return $"N14ForgeArmorTorso{temperedSuffix}{setSuffix}";
            default:
                return null;
        }
    }

    /// <summary>
    ///     Requires the user to be wearing the forge apron and gloves
    ///     (chemical apron + heavy nitrile gloves) to use forge tools.
    /// </summary>
    private bool RequireForgeGear(EntityUid user, EntityUid target)
    {
        if (_inventory.TryGetSlotEntity(user, OuterClothingSlot, out var apron)
            && MetaData(apron.Value).EntityPrototype?.ID == BlacksmithApronProto
            && _inventory.TryGetSlotEntity(user, GlovesSlot, out var gloves)
            && MetaData(gloves.Value).EntityPrototype?.ID == BlacksmithGlovesProto)
        {
            return true;
        }

        _popup.PopupEntity(Loc.GetString("n14-forge-need-forge-gear"), target, user);
        return false;
    }

    /// <summary>
    ///     Finds a nearby anvil within 1.5m of the target entity.
    /// </summary>
    private bool TryGetNearbyAnvil(EntityUid target, EntityUid user, out Entity<N14ForgeAnvilComponent> anvil)
    {
        var set = _lookup.GetEntitiesInRange<N14ForgeAnvilComponent>(Transform(target).Coordinates, 1.5f);
        if (set.Count > 0)
        {
            foreach (var entity in set)
            {
                anvil = entity;
                return true;
            }
        }

        _popup.PopupEntity(Loc.GetString("n14-forge-need-anvil"), target, user);
        anvil = default;
        return false;
    }
}