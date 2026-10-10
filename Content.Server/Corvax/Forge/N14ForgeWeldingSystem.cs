using Content.Server.Popups;
using Content.Shared.Corvax.Forge;
using Content.Shared.DoAfter;
using Content.Shared.Stacks;
using Robust.Shared.Random;

namespace Content.Server.Corvax.Forge;

/// <summary>
///     Welding 4 flat plates (тарелки) into a single torso plate (сваренная плита).
/// </summary>
public sealed class N14ForgeWeldingSystem : EntitySystem
{
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SharedStackSystem _stack = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<N14ForgePlateComponent, N14ForgeWeldDoAfterEvent>(OnWeld);
    }

    private void OnWeld(EntityUid uid, N14ForgePlateComponent component, N14ForgeWeldDoAfterEvent args)
    {
        if (args.Cancelled)
            return;

        if (!TryComp<StackComponent>(uid, out var stack) || stack.Count < 4)
        {
            _popup.PopupEntity(Loc.GetString("n14-forge-weld-not-enough"), uid, args.User);
            return;
        }

        var coords = Transform(uid).Coordinates;
        _stack.Use(uid, 4, stack);

        // Spawn the torso plate of the matching armor set.
        var torsoProto = component.Set switch
        {
            N14ForgeArmorSet.Kratos => "N14ForgeTorsoplateV2",
            N14ForgeArmorSet.Bull => "N14ForgeTorsoplateV3",
            _ => "N14ForgeTorsoplate",
        };
        Spawn(torsoProto, coords);
    }
}