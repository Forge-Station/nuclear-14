namespace Content.Shared._Forge.Silicons.BorgHandModule;

[RegisterComponent]
public sealed partial class BorgHandModuleComponent : Component
{
    [DataField]
    public int Hands = 1;

    [ViewVariables]
    public List<string> AddedHands = new();
}
