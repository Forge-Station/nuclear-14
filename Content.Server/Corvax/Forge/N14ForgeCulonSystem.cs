using Content.Server.Popups;
using Content.Shared.Corvax.Forge;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Tag;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Random;

namespace Content.Server.Corvax.Forge;

/// <summary>
///     Handles working crude kulons on an anvil, by analogy with armor parts:
///     each blacksmith-hammer hit adds progress until the crude kulon becomes a
///     worked one (wearable), each blacksmith-sledgehammer hit after that adds
///     progress until the worked kulon is imbued (final).
/// </summary>
public sealed class N14ForgeCulonSystem : EntitySystem
{
    private const string CulonV1 = "N14ForgeCulonV1";
    private const string CulonV1Worked = "N14ForgeCulonV1Worked";
    private const string CulonV1Final = "N14ForgeCulonV1Final";
    private const string CulonV2 = "N14ForgeCulonV2";
    private const string CulonV2Worked = "N14ForgeCulonV2Worked";
    private const string CulonV2Final = "N14ForgeCulonV2Final";
    private const string BlacksmithApronProto = "ClothingOuterApronChemist";
    private const string BlacksmithGlovesProto = "ClothingHandsGlovesChemist";
    private const string OuterClothingSlot = "outerClothing";
    private const string GlovesSlot = "gloves";

    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<N14ForgeCulonComponent, InteractUsingEvent>(OnCulonUsed);
    }

    private void OnCulonUsed(EntityUid uid, N14ForgeCulonComponent component, InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        var id = MetaData(uid).EntityPrototype?.ID;

        // Already fully imbued — nothing more to do.
        if (id is CulonV1Final or CulonV2Final)
        {
            _popup.PopupEntity(Loc.GetString("n14-forge-culon-already-upgraded"), uid, args.User);
            return;
        }

        if (!TryGetNearbyAnvil(uid, args.User))
            return;

        // Worked (wearable) kulons — a blacksmith sledgehammer imbues special properties.
        if (id is CulonV1Worked or CulonV2Worked)
        {
            if (!_tag.HasTag(args.Used, "N14ForgeSledgehammer"))
            {
                _popup.PopupEntity(Loc.GetString("n14-forge-not-a-sledgehammer"), uid, args.User);
                return;
            }

            // A smith's apron and gloves are required to strike with the sledgehammer.
            if (!RequireForgeGear(args.User, uid))
                return;

            Forge(uid, args, id == CulonV1Worked ? CulonV1Final : CulonV2Final, "n14-forge-culon-upgraded");
            return;
        }

        // Crude kulons — a blacksmith hammer makes them wearable.
        if (id is CulonV1 or CulonV2)
        {
            if (!_tag.HasTag(args.Used, "N14ForgeHammer"))
            {
                _popup.PopupEntity(Loc.GetString("n14-forge-not-a-hammer"), uid, args.User);
                return;
            }

            // A smith's apron and gloves are required to strike with the hammer.
            if (!RequireForgeGear(args.User, uid))
                return;

            Forge(uid, args, id == CulonV1 ? CulonV1Worked : CulonV2Worked, "n14-forge-culon-worked");
            return;
        }
    }

    /// <summary>
    ///     One hammer hit: adds 5-20% progress, then swaps the prototype once the kulon is done.
    /// </summary>
    private void Forge(EntityUid uid, InteractUsingEvent args, string newProto, string finishedLoc)
    {
        var progressComp = EnsureComp<N14ForgeCulonProgressComponent>(uid);
        progressComp.Progress += _random.Next(5, 21);
        Dirty(uid, progressComp);
        args.Handled = true;

        // Not forged yet: sparks + sound + progress popup, keep hammering.
        if (progressComp.Progress < 100f)
        {
            Spawn("N14ForgeSparks", Transform(uid).Coordinates);
            _audio.PlayPvs(new SoundPathSpecifier("/Audio/Corvax/Effects/anvil_hit.ogg"), uid);
            _popup.PopupEntity(Loc.GetString("n14-forge-culon-progress", ("progress", (int)progressComp.Progress)), uid, args.User);
            return;
        }

        // Done: replace the kulon with the next stage.
        var newKulon = SpawnReplacing(uid, newProto);
        _popup.PopupEntity(Loc.GetString(finishedLoc), newKulon, args.User);
    }

    private EntityUid SpawnReplacing(EntityUid old, string newProto)
    {
        var coords = Transform(old).Coordinates;
        var replacement = Spawn(newProto, coords);

        // Forge sparks + sound (on the replacement, so the sound is not cut off).
        Spawn("N14ForgeSparks", coords);
        _audio.PlayPvs(new SoundPathSpecifier("/Audio/Corvax/Effects/anvil_hit.ogg"), replacement);
        Del(old);
        return replacement;
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
    ///     Finds a nearby anvil within 1.5m of the kulon.
    /// </summary>
    private bool TryGetNearbyAnvil(EntityUid target, EntityUid user)
    {
        if (_lookup.GetEntitiesInRange<N14ForgeAnvilComponent>(Transform(target).Coordinates, 1.5f).Count > 0)
            return true;

        _popup.PopupEntity(Loc.GetString("n14-forge-need-anvil"), target, user);
        return false;
    }
}