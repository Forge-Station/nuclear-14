using Content.Client.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Client.GameObjects;

namespace Content.Client.Weapons.Ranged.Systems;

public sealed partial class GunSystem
{
    [Dependency] private readonly SpriteSystem _spriteSystem = default!;

    private void InitializeThrowingAmmoCounter()
    {
        SubscribeLocalEvent<ThrowingAmmoProviderComponent, AmmoCounterControlEvent>(OnThrowingAmmoControl);
        SubscribeLocalEvent<ThrowingAmmoProviderComponent, UpdateAmmoCounterEvent>(OnThrowingAmmoUpdate);
        SubscribeLocalEvent<ThrowingAmmoProviderComponent, AfterAutoHandleStateEvent>(OnThrowingAmmoState);
        SubscribeLocalEvent<HiddenUntilThrownComponent, ComponentStartup>(OnHiddenStartup);
        SubscribeLocalEvent<HiddenUntilThrownComponent, ComponentShutdown>(OnHiddenShutdown);
    }

    private void OnThrowingAmmoControl(EntityUid uid, ThrowingAmmoProviderComponent comp, AmmoCounterControlEvent args)
    {
        args.Control = new BoxesStatusControl();
    }

    private void OnThrowingAmmoUpdate(EntityUid uid, ThrowingAmmoProviderComponent comp, UpdateAmmoCounterEvent args)
    {
        if (args.Control is not BoxesStatusControl boxes)
            return;

        boxes.Update(comp.AmmoCount, comp.Capacity);
        args.Handled = true;
    }

    private void OnThrowingAmmoState(EntityUid uid, ThrowingAmmoProviderComponent comp, ref AfterAutoHandleStateEvent args)
    {
        if (TryComp<AmmoCounterComponent>(uid, out var counter) && counter.Control != null)
            UpdateAmmoCount(uid, counter);
    }

    private void OnHiddenStartup(EntityUid uid, HiddenUntilThrownComponent comp, ComponentStartup args)
    {
        if (TryComp<SpriteComponent>(uid, out var sprite))
            _spriteSystem.SetVisible((uid, sprite), false);
    }

    private void OnHiddenShutdown(EntityUid uid, HiddenUntilThrownComponent comp, ComponentShutdown args)
    {
        if (TryComp<SpriteComponent>(uid, out var sprite))
            _spriteSystem.SetVisible((uid, sprite), true);
    }
}
