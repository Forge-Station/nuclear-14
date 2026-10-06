using Content.Shared._Forge.Chemistry;
using Content.Shared.Chemistry.Hypospray.Events;
using Content.Shared.IdentityManagement;
using Content.Shared.Inventory;

namespace Content.Server._Forge.Chemistry;

/// <summary>
/// Blocks hypospray usage on the wearer of an item with <see cref="HyposprayBlockerComponent"/>.
/// The target hypospray event is relayed to the target's equipped clothing, so this reacts on the
/// worn item itself and only affects the person wearing it (the wearer can still inject others).
/// </summary>
public sealed class HyposprayBlockerSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HyposprayBlockerComponent, InventoryRelayedEvent<TargetBeforeHyposprayInjectsEvent>>(OnTargetInject);
    }

    private void OnTargetInject(EntityUid uid, HyposprayBlockerComponent component, InventoryRelayedEvent<TargetBeforeHyposprayInjectsEvent> args)
    {
        var ev = args.Args;
        if (ev.Cancelled)
            return;

        var hypospray = Identity.Entity(ev.Hypospray, EntityManager);
        var blockerName = Identity.Entity(uid, EntityManager);

        ev.Cancel();

        // Injecting yourself while wearing the blocker reads better with the "on you" wording.
        var self = ev.TargetGettingInjected == ev.EntityUsingHypospray;
        ev.InjectMessageOverride = self
            ? Loc.GetString("hypospray-blocked-self", ("hypospray", hypospray), ("blocker", blockerName))
            : Loc.GetString("hypospray-blocked-target",
                ("hypospray", hypospray),
                ("blocker", blockerName),
                ("target", Identity.Entity(ev.TargetGettingInjected, EntityManager)));
    }
}
