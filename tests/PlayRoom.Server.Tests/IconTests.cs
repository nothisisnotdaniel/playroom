using System.Buffers.Binary;
using System.Net;

namespace PlayRoom.Server.Tests;

/// <summary>The home screen icons (drawn by <c>tools/make-icons.py</c>): served as PNG, sized as named and opaque
/// RGB (no alpha).</summary>
public sealed class IconTests
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    [Theory]
    [InlineData("/icons/apple-touch-icon.png", 180)]
    [InlineData("/icons/icon-192.png", 192)]
    [InlineData("/icons/icon-512.png", 512)]
    public async Task Icon_IsAnOpaqueRgbPngOfItsNamedSize(string path, int size)
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(path);
        byte[] bytes = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        Assert.True(bytes.Length > 33);
        Assert.Equal(Signature, bytes[..8]);
        Assert.Equal("IHDR", System.Text.Encoding.ASCII.GetString(bytes, 12, 4));
        Assert.Equal(size, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)));
        Assert.Equal(size, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)));
        Assert.Equal(2, bytes[25]);
    }
}
