namespace Content.Shared.Chemistry.Components;

[RegisterComponent]
public sealed partial class NocturineNightVisionMetabolismComponent : Component
{
    public TimeSpan ExpiresAt;

    public bool AddedNightVision;

    public bool SavedOriginal;

    public bool OriginalIsActive;

    public Color OriginalColor = Color.Green;
}
