using Content.Shared.Actions;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Popups;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Whitelist;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._Forge.Abilities;

public sealed partial class N14AmbidextrousSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<N14AmbidextrousActionEvent>(OnToggle);
        SubscribeLocalEvent<AmbidextrousComponent, N14PickDualGunEvent>(OnPickGun);
    }

    private void OnToggle(N14AmbidextrousActionEvent args)
    {
        if (args.Handled)
            return;

        var user = args.Performer;
        var enable = !HasComp<AmbidextrousComponent>(user);

        if (enable)
            EnsureComp<AmbidextrousComponent>(user);
        else
            RemComp<AmbidextrousComponent>(user);

        _actions.SetToggled(args.Action.Owner, enable);

        if (_net.IsServer)
            _popup.PopupEntity(Loc.GetString(enable ? "n14-ambidextrous-on" : "n14-ambidextrous-off"), user, user);

        args.Handled = true;
    }

    private void OnPickGun(Entity<AmbidextrousComponent> ent, ref N14PickDualGunEvent args)
    {
        if (args.User != ent.Owner)
            return;

        if (!TryComp<DualWieldableComponent>(args.ActiveGun, out var activeDual))
            return;

        if (!TryFindPartner(ent.Owner, args.ActiveGun, activeDual, out var partner, out var partnerGun))
        {
            ent.Comp.OffhandNext = false;
            Dirty(ent);
            return;
        }

        args.DualWielding = true;

        if (ent.Comp.OffhandNext && partnerGun.NextFire <= _timing.CurTime)
        {
            args.AlternateGun = partner;
            ent.Comp.OffhandNext = false;
        }
        else
            ent.Comp.OffhandNext = true;

        Dirty(ent);
    }

    private bool TryFindPartner(
        EntityUid user,
        EntityUid active,
        DualWieldableComponent activeDual,
        out EntityUid partner,
        out GunComponent partnerGun)
    {
        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (held == active || !TryComp<GunComponent>(held, out var gun))
                continue;

            if (!CanDualFire(active, activeDual, held))
                continue;

            partner = held;
            partnerGun = gun;
            return true;
        }

        partner = default;
        partnerGun = default!;
        return false;
    }

    /// <summary>
    ///     Both guns need the component, and each whitelist must accept the other gun.
    /// </summary>
    private bool CanDualFire(EntityUid firedUid, DualWieldableComponent fired, EntityUid otherUid)
    {
        if (!TryComp<DualWieldableComponent>(otherUid, out var other))
            return false;

        return _whitelist.IsWhitelistPass(fired.Whitelist, otherUid)
               && _whitelist.IsWhitelistPass(other.Whitelist, firedUid);
    }
}

/// <summary>
///     Raised on the shooter before a gun actually fires.
///     Sets <see cref="AlternateGun"/> when this shot should come from the other hand.
/// </summary>
[ByRefEvent]
public record struct N14PickDualGunEvent(EntityUid User, EntityUid ActiveGun)
{
    public EntityUid? AlternateGun;
    public bool DualWielding;
}
