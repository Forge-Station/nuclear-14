using System;

namespace Content.Shared._Forge.Photo;

public static class PngUtility
{
    /// <summary>
    /// Validates PNG file signature (first 8 bytes).
    /// </summary>
    public static bool CheckSignature(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8)
            return false;

        return data[0] == 0x89 &&
               data[1] == 0x50 &&
               data[2] == 0x4E &&
               data[3] == 0x47 &&
               data[4] == 0x0D &&
               data[5] == 0x0A &&
               data[6] == 0x1A &&
               data[7] == 0x0A;
    }

    /// <summary>
    /// Validates PNG signature + IHDR chunk dimensions.
    /// Returns false if image exceeds maxWidth/maxHeight.
    /// </summary>
    public static bool ValidatePng(ReadOnlySpan<byte> data, int maxWidth = 4096, int maxHeight = 4096)
        => TryGetSize(data, out var width, out var height) && width <= maxWidth && height <= maxHeight;

    /// <summary>Read the first IHDR without decoding pixels or allocating an image.</summary>
    public static bool TryGetSize(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = height = 0;
        if (data.Length < 33 || !CheckSignature(data))
            return false;

        // IHDR must be first chunk: bytes 8-11 = length, 12-15 = "IHDR", 16-19 = width, 20-23 = height
        if (data[8] != 0 || data[9] != 0 || data[10] != 0 || data[11] != 13)
            return false;

        if (data[12] != 0x49 || data[13] != 0x48 || data[14] != 0x44 || data[15] != 0x52) // "IHDR"
            return false;

        width = (data[16] << 24) | (data[17] << 16) | (data[18] << 8) | data[19];
        height = (data[20] << 24) | (data[21] << 16) | (data[22] << 8) | data[23];

        return width > 0 && height > 0;
    }
}
