namespace Content.Shared._Forge.Silicons;

/// <summary>
/// Forge-Change: on MapInit, replaces this entity's silicon laws with a set of randomly
/// generated "glitched"/nonsensical laws (same generator the ion storm event uses), instead of
/// any canon lawset. Requires a
/// <see cref="Content.Shared.Silicons.Laws.Components.SiliconLawProviderComponent"/>.
/// Used so a freshly-built junkbot boots with scrambled laws unless overwritten (e.g. by a law card).
/// </summary>
[RegisterComponent]
public sealed partial class RandomSiliconLawsComponent : Component
{
    /// <summary>
    /// Minimum number of random glitched laws to generate (inclusive).
    /// </summary>
    [DataField]
    public int MinLaws = 3;

    /// <summary>
    /// Maximum number of random glitched laws to generate (inclusive).
    /// </summary>
    [DataField]
    public int MaxLaws = 5;
}
