using Content.Shared.Hands.EntitySystems;
using Content.Shared.Popups;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Network;

namespace Content.Shared._Forge.Abilities;

public sealed partial class N14AmbidextrousSystem : EntitySystem
{
    [Dependency] private readonly SharedGunSystem _gun = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    private readonly HashSet<EntityUid> _firing = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<N14AmbidextrousActionEvent>(OnToggle);
        SubscribeLocalEvent<GunComponent, GunShotEvent>(OnGunShot);
    }

    private void OnToggle(N14AmbidextrousActionEvent args)
    {
        if (args.Handled)
            return;

        if (HasComp<AmbidextrousComponent>(args.Performer))
        {
            RemComp<AmbidextrousComponent>(args.Performer);

            if (_net.IsServer)
                _popup.PopupEntity(Loc.GetString("n14-ambidextrous-off"), args.Performer, args.Performer);
        }
        else
        {
            EnsureComp<AmbidextrousComponent>(args.Performer);

            if (_net.IsServer)
                _popup.PopupEntity(Loc.GetString("n14-ambidextrous-on"), args.Performer, args.Performer);
        }

        args.Handled = true;
    }

    private static bool IsDualWieldable(EntityUid uid, MetaDataComponent meta)
    {
        var id = meta.EntityPrototype?.ID;
        return id is not null
               && id.StartsWith("N14Weapon")
               && (id.Contains("Pistol") || id.Contains("Revolver"));
    }

    private void OnGunShot(EntityUid uid, GunComponent gun, ref GunShotEvent args)
    {
        if (!HasComp<AmbidextrousComponent>(args.User))
            return;

        // Only the primary shot fires the other hand; ignore the cascaded one.
        if (!_firing.Add(args.User))
            return;

        try
        {
            if (gun.ShootCoordinates is not { } toCoordinates)
                return;

            foreach (var held in _hands.EnumerateHeld(args.User))
            {
                if (held == uid || !TryComp<GunComponent>(held, out var otherGun))
                    continue;

                if (!IsDualWieldable(held, MetaData(held)))
                    continue;

                _gun.AttemptShoot(args.User, held, otherGun, toCoordinates);
                break;
            }
        }
        finally
        {
            _firing.Remove(args.User);
        }
    }
}
