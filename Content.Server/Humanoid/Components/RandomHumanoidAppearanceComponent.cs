using Content.Shared.Humanoid;
using Robust.Shared.Enums;

namespace Content.Server.CharacterAppearance.Components;

[RegisterComponent]
public sealed partial class RandomHumanoidAppearanceComponent : Component
{
    [DataField("randomizeName")] public bool RandomizeName = true;

    [DataField] public Sex? Sex;

    [DataField] public Gender? Gender;

    [DataField] public int? Age;

    [DataField] public Color? SkinColor;

    [DataField] public string? Hair;

    [DataField] public Color? HairColor;
}
