using System.IO;
using Content.Client._Forge.Paper;
using NUnit.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Tests._Forge.Paper;

[TestFixture]
public sealed class DocumentPhotoHeaderTests
{
    [Test]
    public void CameraPngDimensionsAreReadBeforeDecoding()
    {
        using var image = new Image<Rgba32>(128, 96);
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        var data = stream.ToArray();
        Assert.That(DocumentTextureCacheSystem.TryPhotoSize(data, out var width, out var height), Is.True);
        Assert.That((width, height), Is.EqualTo((128, 96)));

        // An oversized IHDR must be rejected before the decoder allocates pixels.
        data[16] = 0; data[17] = 0; data[18] = 8; data[19] = 1;
        Assert.That(DocumentTextureCacheSystem.TryPhotoSize(data, out _, out _), Is.False);
        data[16] = 128;
        Assert.That(DocumentTextureCacheSystem.TryPhotoSize(data, out _, out _), Is.False);
    }

    [Test]
    public void MissingOrInvalidPngHeaderIsRejected()
    {
        Assert.That(DocumentTextureCacheSystem.TryPhotoSize(new byte[12], out _, out _), Is.False);
        Assert.That(DocumentTextureCacheSystem.TryPhotoSize(new byte[40], out _, out _), Is.False);
    }
}
