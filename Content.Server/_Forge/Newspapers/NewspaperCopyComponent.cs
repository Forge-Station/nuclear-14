using Content.Shared._Forge.Newspapers;

namespace Content.Server._Forge.Newspapers;

[RegisterComponent]
public sealed partial class NewspaperCopyComponent : Component
{
    [DataField] public NewspaperEdition Edition = new();
    [DataField] public byte[]? ImageData;
    [DataField] public Dictionary<int, byte[]> Photos = new();
    public Dictionary<int, string> PhotoKeys = new();
    [ViewVariables] public string PhotoKey = "";
    public NewspaperCopyUiState? CachedUiState;
}
