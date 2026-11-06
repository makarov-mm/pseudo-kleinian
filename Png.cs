// Minimal PNG writer (8-bit RGB, no filtering), BCL only: zlib through
// ZLibStream, CRC-32 by hand.
using System.Buffers.Binary;
using System.IO.Compression;

namespace Kleinian;

internal static class Png
{
    private static readonly byte[] s_signature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly uint[] s_crcTable = BuildCrcTable();

    // `rgbBottomUp` is laid out the way glReadPixels returns it: the first
    // row is the bottom of the image. PNG stores rows top-down.
    public static void WriteRgb(string path, int width, int height, ReadOnlySpan<byte> rgbBottomUp)
    {
        int stride = width * 3;
        if (rgbBottomUp.Length < stride * height) throw new ArgumentException("pixel buffer too small", nameof(rgbBottomUp));

        byte[] idat;
        using (var compressed = new MemoryStream(stride * height / 2))
        {
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            {
                ReadOnlySpan<byte> filterNone = [0];
                for (int y = height - 1; y >= 0; y--)
                {
                    zlib.Write(filterNone);
                    zlib.Write(rgbBottomUp.Slice(y * stride, stride));
                }
            }
            idat = compressed.ToArray();
        }

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr[4..], height);
        ihdr[8] = 8;   // bit depth
        ihdr[9] = 2;   // colour type: RGB
        ihdr[10] = 0;  // compression
        ihdr[11] = 0;  // filter method
        ihdr[12] = 0;  // no interlace

        using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
        file.Write(s_signature);
        WriteChunk(file, "IHDR"u8, ihdr);
        WriteChunk(file, "IDAT"u8, idat);
        WriteChunk(file, "IEND"u8, []);
    }

    private static void WriteChunk(Stream stream, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(word, data.Length);
        stream.Write(word);
        stream.Write(type);
        stream.Write(data);
        BinaryPrimitives.WriteUInt32BigEndian(word, Crc32(Crc32(0xFFFFFFFFu, type), data) ^ 0xFFFFFFFFu);
        stream.Write(word);
    }

    private static uint Crc32(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (byte b in bytes)
            crc = s_crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }
}
