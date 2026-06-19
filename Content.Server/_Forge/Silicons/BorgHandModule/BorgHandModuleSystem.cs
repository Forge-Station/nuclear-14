using Content.Shared._Forge.Silicons.BorgHandModule;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Silicons.Borgs.Components;

namespace Content.Server._Forge.Silicons.BorgHandModule;

public sealed class BorgHandModuleSystem : EntitySystem
{
    [Dependency] private readonly SharedHandsSystem _hands = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BorgHandModuleComponent, BorgModuleSelectedEvent>(OnSelected);
        SubscribeLocalEvent<BorgHandModuleComponent, BorgModuleUnselectedEvent>(OnUnselected);
    }

    private void OnSelected(Entity<BorgHandModuleComponent> ent, ref BorgModuleSelectedEvent args)
    {
        // руки уже выданы или вешать их некуда — выходим.
        if (ent.Comp.AddedHands.Count > 0 || !TryComp<HandsComponent>(args.Chassis, out var hands))
            return;

        for (var i = 0; i < ent.Comp.Hands; i++)
        {
            var handId = $"{ent.Owner}-hand{i}";
            _hands.AddHand(args.Chassis, handId, HandLocation.Middle, hands);
            ent.Comp.AddedHands.Add(handId);
        }
    }

    private void OnUnselected(Entity<BorgHandModuleComponent> ent, ref BorgModuleUnselectedEvent args)
    {
        if (!TryComp<HandsComponent>(args.Chassis, out var hands))
            return;

        // RemoveHand сам роняет то, что было в руке, перед её удалением — так предмет не пропадёт.
        foreach (var handId in ent.Comp.AddedHands)
            _hands.RemoveHand(args.Chassis, handId, hands);

        ent.Comp.AddedHands.Clear();
    }
}
