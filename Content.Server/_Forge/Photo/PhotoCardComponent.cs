namespace Content.Server._Forge.Photo;

[RegisterComponent]
public sealed partial class PhotoCardComponent : Component
{
    /// <summary>
    /// Persistent image payload used for map save/load.
    /// Runtime transfer still uses <see cref="ImageId"/>.
    /// </summary>
    [DataField]
    public byte[]? ImageData;

    /// <summary>
    /// Round-local photo blob ID in <see cref="PhotoBlobStoreSystem"/>.
    /// -1 means no image attached.
    /// </summary>
    [ViewVariables]
    public int ImageId = -1;
}
