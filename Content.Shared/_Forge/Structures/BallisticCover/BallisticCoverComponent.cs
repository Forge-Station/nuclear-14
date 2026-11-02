namespace Content.Shared._Forge.Structures.BallisticCover;

/// <summary>
/// Low cover: lets shots pass near the start of their trajectory and takes other hits.
/// Damage and destruction remain in the engine Damageable/Destructible systems.
/// </summary>
[RegisterComponent]
public sealed partial class BallisticCoverComponent : Component
{
    [DataField]
    public float PassRange = 2f;

    [DataField]
    public string Fixture = "fix1";

    // Chance for an ordinary bullet to pass through after impact, and its remaining damage fraction.
    [DataField]
    public float PenetrationChance;

    [DataField]
    public float PenetrationDamageMultiplier = 0.5f;
}
