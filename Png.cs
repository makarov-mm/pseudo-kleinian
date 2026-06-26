using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Kleinian;

// Minimal PNG writer (24-bit RGB), BCL-only: zlib via ZLibStream, manual CRC32.
public static class Png
{
    static uint[] _crc;

    public static void WriteRgbFlip(string path, int w, int h, byte[] rgbBottomUp)
    {
        int stride = w * 3;
        var raw = new byte[(stride + 1) * h];

        for (int y = 0; y < h; y++)
        {
            int src = (h - 1 - y) * stride; // glReadPixels is bottom-up; PNG is top-down
            int dst = y * (stride + 1);
            raw[dst] = 0; // filter type 0 (none)
            Buffer.BlockCopy(rgbBottomUp, src, raw, dst + 1, stride);
        }

        byte[] comp;

        using (var ms = new MemoryStream())
        {
            using (var z = new ZLibStream(ms, CompressionLevel.Optimal, true))
            {
                z.Write(raw, 0, raw.Length);
            }

            comp = ms.ToArray();
        }

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        fs.Write([137, 80, 78, 71, 13, 10, 26, 10], 0, 8);

        var ihdr = new byte[13];
        BE(ihdr, 0, (uint)w);
        BE(ihdr, 4, (uint)h);
        ihdr[8] = 8; // bit depth
        ihdr[9] = 2; // colour type 2 = RGB
        Chunk(fs, "IHDR", ihdr);
        Chunk(fs, "IDAT", comp);
        Chunk(fs, "IEND", Array.Empty<byte>());
    }

    private static void BE(byte[] b, int o, uint v)
    {
        b[o] = (byte)(v >> 24);
        b[o + 1] = (byte)(v >> 16);
        b[o + 2] = (byte)(v >> 8);
        b[o + 3] = (byte)v;
    }

    private static void Chunk(Stream stream, string type, byte[] data)
    {
        var len = new byte[4];
        BE(len, 0, (uint)data.Length);
        stream.Write(len, 0, 4);
        byte[] t = Encoding.ASCII.GetBytes(type);
        stream.Write(t, 0, 4);
        stream.Write(data, 0, data.Length);
        uint crc = Crc(t, data);
        var c = new byte[4];
        BE(c, 0, crc);
        stream.Write(c, 0, 4);
    }

    private static uint Crc(byte[] type, byte[] data)
    {
        if (_crc is null)
        {
            _crc = new uint[256];

            for (uint n = 0; n < 256; n++)
            {
                uint c = n;

                for (int k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                }

                _crc[n] = c;
            }
        }

        uint crc = 0xFFFFFFFFu;

        foreach (byte b in type)
        {
            crc = _crc[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        foreach (byte b in data)
        {
            crc = _crc[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }
}
