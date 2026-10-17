using Content.Server.Administration.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Administration.Components;

namespace Content.Server.Administration.Systems;

public sealed class AdminGunSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<AdminMinigunComponent, GunRefreshModifiersEvent>(OnGunRefreshModifiers);
    }

    private void OnGunRefreshModifiers(Entity<AdminMinigunComponent> ent, ref GunRefreshModifiersEvent args)
    {
        args.FireRate = 15;
    }
}
