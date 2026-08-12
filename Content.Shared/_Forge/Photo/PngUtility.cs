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
    {
        if (!CheckSignature(data))
            return false;

        // IHDR must be first chunk: bytes 8-11 = length, 12-15 = "IHDR", 16-19 = width, 20-23 = height
        if (data.Length < 24)
            return false;

        if (data[12] != 0x49 || data[13] != 0x48 || data[14] != 0x44 || data[15] != 0x52) // "IHDR"
            return false;

        var width = (data[16] << 24) | (data[17] << 16) | (data[18] << 8) | data[19];
        var height = (data[20] << 24) | (data[21] << 16) | (data[22] << 8) | data[23];

        return width > 0 && width <= maxWidth && height > 0 && height <= maxHeight;
    }
}
