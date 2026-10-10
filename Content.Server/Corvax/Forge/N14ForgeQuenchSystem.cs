using Content.Server.Popups;
using Content.Shared.Corvax.Forge;
using Content.Shared.Interaction;

namespace Content.Server.Corvax.Forge;

/// <summary>
///     Quenching: a heated forged plate clicked on water becomes закалённая.
///     The prototype and sprite stay the same - only the variant marker changes.
/// </summary>
public sealed class N14ForgeQuenchSystem : EntitySystem
{
    [Dependency] private readonly PopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        // InteractUsingEvent is raised on the target entity (the water), so to
        // catch "heated plate used on water" we need to subscribe on a component
        // every entity has (MetaDataComponent) and then do our own checks.
        SubscribeLocalEvent<MetaDataComponent, InteractUsingEvent>(OnQuenchCheck);
    }

    private void OnQuenchCheck(EntityUid target, MetaDataComponent component, InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        // The used item must be a heated plate.
        if (!HasComp<N14ForgeHeatedComponent>(args.Used))
            return;

        if (!IsWater(target))
        {
            _popup.PopupEntity(Loc.GetString("n14-forge-quench-not-water"), target, args.User);
            return;
        }

        // Only an ordinary forged plate (кованая) can be tempered.
        if (!TryComp<N14ForgePlateComponent>(args.Used, out var plate) ||
            plate.Variant != N14ForgePlateVariant.Forged)
        {
            _popup.PopupEntity(Loc.GetString("n14-forge-quench-invalid"), args.Used, args.User);
            return;
        }

        args.Handled = true;

        RemComp<N14ForgeHeatedComponent>(args.Used);
        plate.Variant = N14ForgePlateVariant.Tempered;
        Dirty(args.Used, plate);
        EnsureComp<N14ForgeForgedComponent>(args.Used);

        _popup.PopupEntity(Loc.GetString("n14-forge-quenched"), args.User, args.User);
    }

    private bool IsWater(EntityUid target)
    {
        if (!TryComp(target, out MetaDataComponent? meta) || meta.EntityPrototype == null)
            return false;

        var id = meta.EntityPrototype.ID;
        return id.StartsWith("N14") && id.Contains("Water");
    }
}