using Content.Shared._Forge.Chemistry;
using Content.Shared.Chemistry.Hypospray.Events;
using Content.Shared.IdentityManagement;
using Content.Shared.Inventory;

namespace Content.Server._Forge.Chemistry;

/// <summary>
/// Blocks hypospray usage while an item with <see cref="HyposprayBlockerComponent"/> is equipped.
/// The hypospray events are relayed to equipped clothing, so this reacts on the worn item itself.
/// </summary>
public sealed class HyposprayBlockerSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HyposprayBlockerComponent, InventoryRelayedEvent<SelfBeforeHyposprayInjectsEvent>>(OnSelfInject);
        SubscribeLocalEvent<HyposprayBlockerComponent, InventoryRelayedEvent<TargetBeforeHyposprayInjectsEvent>>(OnTargetInject);
    }

    private void OnSelfInject(EntityUid uid, HyposprayBlockerComponent component, InventoryRelayedEvent<SelfBeforeHyposprayInjectsEvent> args)
    {
        Block(uid, args.Args, self: true);
    }

    private void OnTargetInject(EntityUid uid, HyposprayBlockerComponent component, InventoryRelayedEvent<TargetBeforeHyposprayInjectsEvent> args)
    {
        Block(uid, args.Args, self: false);
    }

    private void Block(EntityUid blocker, BeforeHyposprayInjectsTargetEvent args, bool self)
    {
        if (args.Cancelled)
            return;

        var hypospray = Identity.Entity(args.Hypospray, EntityManager);
        var blockerName = Identity.Entity(blocker, EntityManager);

        args.Cancel();
        args.InjectMessageOverride = self
            ? Loc.GetString("hypospray-blocked-self", ("hypospray", hypospray), ("blocker", blockerName))
            : Loc.GetString("hypospray-blocked-target",
                ("hypospray", hypospray),
                ("blocker", blockerName),
                ("target", Identity.Entity(args.TargetGettingInjected, EntityManager)));
    }
}
