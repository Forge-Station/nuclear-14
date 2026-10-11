namespace Content.Shared.Corvax.Forge;

/// <summary>
///     Shared system providing helpers for the Legion Forge crafting chain.
/// </summary>
public abstract partial class SharedN14ForgeSystem : EntitySystem
{
    /// <summary>
    ///     Checks whether the given entity is a water tile/entity usable for quenching.
    /// </summary>
    public bool IsWater(EntityUid uid)
    {
        if (!TryComp(uid, out MetaDataComponent? meta) || meta.EntityPrototype == null)
            return false;

        var id = meta.EntityPrototype.ID;
        return id.StartsWith("N14") && id.Contains("Water");
    }
}