using Content.Shared.Interaction;
using Content.Shared.Tools.Systems;
using Content.Shared.WeaponMounts;

namespace Content.Server.WeaponMounts;

public sealed class WeaponMountSystem : SharedWeaponMountSystem
{
    [Dependency] private readonly SharedToolSystem _tool = default!;

    public override void Initialize()
    {
        base.Initialize();


        SubscribeLocalEvent<WeaponMountComponent, RepairMountDoAfterEvent>(OnRepair);
    }

    protected override void OnInteractUsing(Entity<WeaponMountComponent> ent, ref InteractUsingEvent args)
    {
        base.OnInteractUsing(ent, ref args);

        if (args.Handled || !ent.Comp.Broken)
            return;
        if (_tool.UseTool(args.Used, args.User, ent, 3.0f, ["Welding"], new RepairMountDoAfterEvent()))
            args.Handled = true;
    }

    private void OnRepair(Entity<WeaponMountComponent> ent, ref RepairMountDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;

        ent.Comp.Broken = false;
        Dirty(ent);
        args.Handled = true;
    }
}
