namespace Content.Client.PDA;

/// <summary>
/// Used for specifying the pda windows border colors
/// </summary>
[RegisterComponent]
public sealed partial class PdaBorderColorComponent : Component
{
    /// Forge-Change-Start
    // Optional Pip-Boy skin; false restores the standard PDA interface.
    [DataField]
    public bool PipBoyTheme;

    // Local preference retained when this PDA window is reopened.
    public bool PipBoyGreen;

    /// Forge-Change-End
    [DataField("borderColor", required: true)]
    public string? BorderColor;

    [DataField("accentHColor")]
    public string? AccentHColor;

    [DataField("accentVColor")]
    public string? AccentVColor;
}
