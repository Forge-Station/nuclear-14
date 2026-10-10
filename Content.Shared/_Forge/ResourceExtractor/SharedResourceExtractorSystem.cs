using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Components;
using Content.Shared.Popups;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.UserInterface;

namespace Content.Shared._Forge.ResourceExtractor;

/// <summary>
/// Shared interaction logic. It must run before generic storage interaction so
/// activating the machine opens its controls rather than the output hopper.
/// </summary>
public abstract class SharedResourceExtractorSystem : EntitySystem
{
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solution = default!;
    [Dependency] private readonly SolutionTransferSystem _solutionTransfer = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ResourceExtractorComponent, ActivateInWorldEvent>(
            OnActivate,
            before: new[] { typeof(SharedStorageSystem) });
        SubscribeLocalEvent<ResourceExtractorComponent, InteractUsingEvent>(
            OnInteractUsing,
            before: new[] { typeof(SharedStorageSystem) });
    }

    private void OnActivate(EntityUid uid, ResourceExtractorComponent component, ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex)
            return;

        args.Handled = _ui.TryToggleUi(uid, ResourceExtractorUiKey.Key, args.User);
    }

    /// <summary>
    /// Admin ghosts bypass storage interaction checks, including clickInsert.
    /// Handle their chemical refuelling before Storage can put the container
    /// into the output hopper. Normal users keep using SolutionTransferSystem's
    /// standard AfterInteract path.
    /// </summary>
    private void OnInteractUsing(EntityUid uid, ResourceExtractorComponent component, InteractUsingEvent args)
    {
        if (args.Handled ||
            !HasComp<BypassInteractionChecksComponent>(args.User) ||
            !TryComp<SolutionTransferComponent>(args.Used, out var transfer) ||
            !transfer.CanSend ||
            !TryComp<RefillableSolutionComponent>(uid, out var refillable) ||
            !_solution.TryGetRefillableSolution((uid, refillable, null), out var targetSolution, out _) ||
            !_solution.TryGetDrainableSolution(args.Used, out var sourceSolution, out _))
        {
            return;
        }

        var amount = transfer.TransferAmount;
        if (refillable.MaxRefill is { } maxRefill)
            amount = FixedPoint2.Min(amount, maxRefill);

        var transferred = _solutionTransfer.Transfer(
            args.User,
            args.Used,
            sourceSolution.Value,
            uid,
            targetSolution.Value,
            amount);

        if (transferred > 0)
        {
            var message = Loc.GetString(
                "comp-solution-transfer-transfer-solution",
                ("amount", transferred),
                ("target", uid));
            _popup.PopupClient(message, args.Used, args.User);
        }

        // Transfer already supplies the empty/full/cancelled feedback. Always
        // consume this valid refuelling attempt so Storage cannot grab the item.
        args.Handled = true;
    }
}
