using System.Linq;
using Content.Shared.Clothing.Components;
using Robust.Shared.Serialization;

namespace Content.Shared.Clothing.EntitySystems;

/// <summary>
/// Форковая надстройка: герметичной одежде (Goobstation) нужно знать, выпущены ли
/// все пристёгнутые части.
/// </summary>
/// <remarks>
/// Форк расширял <see cref="ToggleableClothingComponent"/> до нескольких
/// пристёгнутых предметов, апстрим держит один (ClothingUid), поэтому здесь
/// состояние считается по одному предмету. Многочастная версия - отдельный порт.
/// </remarks>
public sealed partial class ToggleableClothingSystem
{
    /// <summary>
    /// Состояние пристёгнутых предметов: ни один не выпущен, часть или все.
    /// </summary>
    public ToggleableClothingAttachedStatus GetAttachedToggleStatus(EntityUid toggleable,
        ToggleableClothingComponent? component = null)
    {
        if (!Resolve(toggleable, ref component)
            || component.Container is not { } container
            || component.ClothingUid is not { } clothing)
            return ToggleableClothingAttachedStatus.NoneToggled;

        // Выпущенный предмет лежит не в контейнере, а на носителе.
        return container.Contains(clothing)
            ? ToggleableClothingAttachedStatus.NoneToggled
            : ToggleableClothingAttachedStatus.AllToggled;
    }

    /// <summary>
    /// Все пристёгнутые предметы, или null, если их нет.
    /// </summary>
    public List<EntityUid>? GetAttachedClothingsList(EntityUid toggleable,
        ToggleableClothingComponent? component = null)
    {
        if (!Resolve(toggleable, ref component) || component.ClothingUid is not { } clothing)
            return null;

        return [clothing];
    }
}

/// <summary>
/// Состояние пристёгнутых к одежде предметов.
/// </summary>
[Serializable, NetSerializable]
public enum ToggleableClothingAttachedStatus : byte
{
    NoneToggled,
    PartlyToggled,
    AllToggled,
}
