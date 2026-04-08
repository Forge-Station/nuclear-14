using System;

namespace Content.Shared._Forge.Rendering.Cache;

public static class TextureCachePngUtility
{
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
}
