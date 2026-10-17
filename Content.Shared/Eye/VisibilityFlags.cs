using Robust.Shared.Serialization;

namespace Content.Shared.Eye
{
    [Flags]
    [FlagsFor(typeof(VisibilityMaskLayer))]
    public enum VisibilityFlags : int
    {
        None = 0,
        Normal = 1 << 0,
        Ghost = 1 << 1, // Observers and revenants.
        Subfloor = 1 << 2, // Pipes, disposal chutes, cables etc. while hidden under tiles. Can be revealed with a t-ray.
        Admin = 1 << 3, // Reserved for admins in stealth mode and admin tools.

        // Forge-Change-Start: форковые слои. Биты пришлось сдвинуть - апстрим занял
        // 1 << 2 и 1 << 3 под Subfloor и Admin. В прототипах слои задаются именами,
        // так что сдвиг на данные не влияет.
        PsionicInvisibility = 1 << 4,
        TelegnosticProjection = 1 << 5,
        Ethereal = 1 << 6,
        // Forge-Change-End
    }
}
