namespace Content.Shared.Chemistry.Components;


[RegisterComponent]
public sealed partial class NocturineNightVisionStatusEffectComponent : Component
{
    [ViewVariables] public bool AddedNightVision;
    [DataField] public Color NightVisionColor = Color.FromHex("#98FB98");
    [ViewVariables] public Color OriginalColor = Color.White;
    [ViewVariables] public bool OriginalIsActive;
    [ViewVariables] public bool SavedOriginal;
}
