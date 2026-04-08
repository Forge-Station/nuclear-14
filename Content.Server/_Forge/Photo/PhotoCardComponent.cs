namespace Content.Server._Forge.Photo;

[RegisterComponent]
public sealed partial class PhotoCardComponent : Component
{
    /// <summary>
    /// Raw image data. Persisted via DataField for map saves.
    /// </summary>
    [DataField]
    public byte[]? ImageData;

    /// <summary>
    /// Runtime-assigned image ID for network deduplication.
    /// Not persisted — reassigned on MapInit from ImageData.
    /// </summary>
    [ViewVariables]
    public int ImageId = -1;
}
