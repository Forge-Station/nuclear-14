namespace Content.Shared._Forge.Newspapers;

public static class NewspaperPhotoBuffer
{
    public const int MaxPhotos = 12;
    public const long MaxBytes = 32L * 1024 * 1024;
    public static bool CanImport(int count, long bytes, int imageBytes) =>
        count >= 0 && count < MaxPhotos && bytes >= 0 && bytes <= MaxBytes &&
        imageBytes > 0 && imageBytes <= 4 * 1024 * 1024 && imageBytes <= MaxBytes - bytes;
}
