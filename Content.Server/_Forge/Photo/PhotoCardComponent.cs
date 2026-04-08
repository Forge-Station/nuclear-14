namespace Content.Server._Forge.Photo;

[RegisterComponent]
public sealed partial class PhotoCardComponent : Component
{
    [DataField]
    public byte[]? ImageData;
}
