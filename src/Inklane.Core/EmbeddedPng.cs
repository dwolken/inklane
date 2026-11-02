using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace Inklane.Core;

public static class EmbeddedPng
{
    public const int MaxBytes = 16 * 1024 * 1024;
    public const int MaxPixels = 24_000_000;

    // Read the fixed IHDR before asking WIC to allocate a pixel buffer.
    internal static long CheckHeader(byte[] bytes)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82];
        ReadOnlySpan<byte> end = [0, 0, 0, 0, 73, 69, 78, 68, 174, 66, 96, 130];
        if (bytes is null || bytes.Length is < 45 or > MaxBytes ||
            !bytes.AsSpan().StartsWith(signature) || !bytes.AsSpan().EndsWith(end))
            throw new InvalidDataException("The embedded PNG is missing, damaged, or exceeds 16 MiB.");
        var width = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16));
        var height = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20));
        var pixels = (ulong)width * height;
        if (width is 0 or > 32768 || height is 0 or > 32768 || pixels > MaxPixels)
            throw new InvalidDataException("The embedded image dimensions exceed the supported limit.");
        return (long)pixels;
    }

    public static BitmapSource Decode(byte[] bytes)
    {
        CheckHeader(bytes);
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            var bitmap = decoder.Frames[0];
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or FormatException or COMException or ArgumentException)
        {
            throw new InvalidDataException("The embedded PNG could not be decoded.", ex);
        }
    }
}
