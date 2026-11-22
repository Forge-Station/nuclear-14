namespace Content.Server._Forge.ChemEffects;

/// <summary>
///     Active sources that temporarily scale a mob's maximum stamina
///     via <see cref="Content.Shared.Damage.Components.StaminaModifierComponent"/>.
/// </summary>
[RegisterComponent]
public sealed partial class TimedStaminaModifierComponent : Component
{
    public Dictionary<string, (float Modifier, TimeSpan Expiry)> Sources = new();

    public bool Initialized;
    public bool HadModifier;
    public float BaseModifier = 1f;
}
