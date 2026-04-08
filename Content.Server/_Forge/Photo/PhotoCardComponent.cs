namespace Content.Server._Forge.Photo;

[RegisterComponent]
public sealed partial class PhotoCardComponent : Component
{
    /// <summary>
    /// Round-local photo blob ID in <see cref="PhotoBlobStoreSystem"/>.
    /// -1 means no image attached.
    /// </summary>
    [ViewVariables]
    public int ImageId = -1;
}
