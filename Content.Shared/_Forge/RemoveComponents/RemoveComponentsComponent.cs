namespace Content.Shared._Forge.RemoveComponents;

[RegisterComponent]
public sealed partial class RemoveComponentsComponent : Component
{
    [DataField]
    public HashSet<string> Components = new();
}
