namespace Content.Shared._Forge.Silicons.RandomLaws;

[RegisterComponent]
public sealed partial class RandomSiliconLawsComponent : Component
{
    [DataField]
    public int MinLaws = 3;

    [DataField]
    public int MaxLaws = 5;
}
