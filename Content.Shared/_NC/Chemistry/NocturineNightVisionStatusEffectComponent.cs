namespace Content.Shared.Chemistry.Components;

[RegisterComponent]
public sealed partial class NocturineNightVisionStatusEffectComponent : Component
{
    [DataField] public Color NightVisionColor = Color.FromHex("#98FB98");
    [DataField] public bool AddedNightVision;
    [DataField] public bool SavedOriginal;
    [DataField] public bool OriginalIsActive;
    [DataField] public Color OriginalColor = Color.White;
}
