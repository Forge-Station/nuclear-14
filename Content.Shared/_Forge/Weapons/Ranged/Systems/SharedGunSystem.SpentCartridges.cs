// Forge integration hook for the adapted Misfits client-only casing implementation.
namespace Content.Shared.Weapons.Ranged.Systems;

public abstract partial class SharedGunSystem
{
    /// <summary>
    /// The server sends a cosmetic event and deletes the ejected casing. Clients wait for
    /// that event so prediction and server confirmation cannot create duplicate visuals.
    /// </summary>
    protected virtual void EjectSpentCartridge(EntityUid entity, bool playSound) { }
}
