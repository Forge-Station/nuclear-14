using Content.Shared.Foldable;
using Content.Shared.WeaponMounts;
using Content.Shared.WeaponMounts.Overheat;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Client.GameObjects;
using DrawDepth = Content.Shared.DrawDepth.DrawDepth;


namespace Content.Client.WeaponMounts;


/// <summary>
///     Клиентская реализация: обновляет спрайтовые слои станка по данным
///     из <see cref="AppearanceComponent" /> и сетевого состояния.
/// </summary>
public sealed class WeaponMountSystem : SharedWeaponMountSystem
{
    /// <summary>Слой складывания, создаваемый стандартным FoldableComponent визуалайзером.</summary>
    private const string VanillaFoldedLayer = "foldedLayer";

    [Dependency] private readonly SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WeaponMountComponent, AfterAutoHandleStateEvent>(OnStateChanged);
        SubscribeLocalEvent<WeaponMountComponent, AppearanceChangeEvent>(OnAppearanceChanged);
    }

    private void OnStateChanged(Entity<WeaponMountComponent> ent, ref AfterAutoHandleStateEvent args) =>
        UpdateVisuals(ent);

    private void OnAppearanceChanged(Entity<WeaponMountComponent> ent, ref AppearanceChangeEvent args) =>
        UpdateVisuals(ent);

    // ── Обновление визуала ────────────────────────────────────────────────────

    private void UpdateVisuals(Entity<WeaponMountComponent> mount)
    {
        if (!TryComp(mount, out SpriteComponent? sprite))
            return;

        TryComp(mount, out FoldableComponent? foldable);

        Entity<SpriteComponent?> spr = (mount, sprite);

        UpdateDeployedLayers(mount, spr, sprite, foldable);
        UpdateFoldedLayers(mount, spr, foldable);
        UpdateDrawDepth(mount, spr, foldable);
    }

    /// <summary>Обновляет слои для развёрнутого (боевого) состояния.</summary>
    private void UpdateDeployedLayers(
        Entity<WeaponMountComponent> mount,
        Entity<SpriteComponent?> spr,
        SpriteComponent sprite,
        FoldableComponent? foldable
    )
    {
        if (!_sprite.LayerMapTryGet(spr, WeaponMountLayers.Deployed, out var deployedLayer, false))
            return;

        var isDeployed = mount.Comp.MountedEntity != null
            && (foldable == null || !foldable.IsFolded)
            && !mount.Comp.Broken;

        // Перегрев — плавное изменение прозрачности свечения.
        if (_sprite.LayerMapTryGet(spr, WeaponMountLayers.Overheated, out var heatLayer, false)
            && TryComp(mount.Comp.MountedEntity, out OverheatComponent? overheat))
        {
            _sprite.LayerSetVisible(spr, heatLayer, isDeployed);
            var alpha = Math.Clamp(overheat.Heat / overheat.MaxHeat, 0f, 1f);
            _sprite.LayerSetColor(spr, heatLayer, sprite.Color.WithAlpha(alpha));
        }

        // Индикатор патронов — только в развёрнутом состоянии.
        if (foldable != null && foldable.IsFolded)
            _sprite.LayerSetVisible(spr, WeaponMountLayers.DeployedAmmo, false);
        else
            UpdateAmmoLayer(mount, spr, WeaponMountLayers.DeployedAmmo);

        _sprite.LayerSetVisible(spr, deployedLayer, isDeployed);

        if (_sprite.LayerMapTryGet(spr, WeaponMountLayers.Broken, out var brokenLayer, false))
            _sprite.LayerSetVisible(spr, brokenLayer, mount.Comp.Broken);
    }

    /// <summary>Обновляет слои для сложенного (транспортного) состояния.</summary>
    private void UpdateFoldedLayers(
        Entity<WeaponMountComponent> mount,
        Entity<SpriteComponent?> spr,
        FoldableComponent? foldable
    )
    {
        if (!_sprite.LayerMapTryGet(spr, WeaponMountLayers.Folded, out var foldedLayer, false)
            || foldable == null)
            return;

        var isFolded = foldable.IsFolded && !mount.Comp.Broken;

        // Индикатор патронов в сложенном состоянии.
        if (foldable.IsFolded)
            UpdateAmmoLayer(mount, spr, WeaponMountLayers.FoldedAmmo);
        else
            _sprite.LayerSetVisible(spr, WeaponMountLayers.FoldedAmmo, false);

        // Слой "сложен + с оружием".
        _sprite.LayerSetVisible(spr, foldedLayer, isFolded && mount.Comp.MountedEntity != null);

        // Стандартный vanilla-слой складывания: показываем только если нет оружия.
        if (_sprite.LayerMapTryGet(spr, VanillaFoldedLayer, out var vanillaLayer, false))
            _sprite.LayerSetVisible(spr, vanillaLayer, isFolded && mount.Comp.MountedEntity == null);

        if (_sprite.LayerMapTryGet(spr, WeaponMountLayers.Broken, out var brokenLayer, false))
            _sprite.LayerSetVisible(spr, brokenLayer, mount.Comp.Broken);
    }

    /// <summary>
    ///     Выбирает глубину отрисовки: Items (сложен / нет оружия) или Mobs (развёрнут с оружием).
    /// </summary>
    private void UpdateDrawDepth(
        Entity<WeaponMountComponent> mount,
        Entity<SpriteComponent?> spr,
        FoldableComponent? foldable
    )
    {
        var asItem = mount.Comp.MountedEntity == null
            || foldable != null && foldable.IsFolded;

        _sprite.SetDrawDepth(spr, asItem ? (int) DrawDepth.Items : (int) DrawDepth.Mobs);
    }

    /// <summary>Обновляет видимость индикатора патронов для заданного слоя.</summary>
    private void UpdateAmmoLayer(Entity<WeaponMountComponent> mount, Entity<SpriteComponent?> spr, Enum key)
    {
        if (!_sprite.LayerMapTryGet(spr, key, out var layer, false))
            return;

        var hasAmmo = false;
        if (mount.Comp.MountedEntity != null)
        {
            var ev = new GetAmmoCountEvent();
            RaiseLocalEvent(mount.Comp.MountedEntity.Value, ref ev);
            hasAmmo = ev.Count > 0;
        }

        _sprite.LayerSetVisible(spr, layer, hasAmmo);
    }
}
