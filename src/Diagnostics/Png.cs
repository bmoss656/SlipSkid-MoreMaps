using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace SlipSkid.MoreMaps;

// Texture2D.EncodeToPNG is stripped from the game's build, so: a minimal 8-bit RGB/RGBA PNG writer.
internal static class Png
{
    internal static byte[] Encode(Color32[] pixels, int width, int height, bool alpha = false)
    {
        // Raw scanlines, top row first (Unity's pixel rows run bottom-up), each with filter byte 0.
        var channels = alpha ? 4 : 3;
        var raw = new byte[height * (width * channels + 1)];
        var o = 0;
        for (var y = height - 1; y >= 0; y--)
        {
            raw[o++] = 0;
            for (var x = 0; x < width; x++)
            {
                var c = pixels[y * width + x];
                raw[o++] = c.r;
                raw[o++] = c.g;
                raw[o++] = c.b;
                if (alpha)
                    raw[o++] = c.a;
            }
        }

        var zlib = new MemoryStream();
        zlib.WriteByte(0x78);
        zlib.WriteByte(0x01);
        using (var deflate = new DeflateStream(zlib, CompressionLevel.Fastest, true))
            deflate.Write(raw, 0, raw.Length);
        WriteBE(zlib, Adler32(raw));

        var png = new MemoryStream();
        png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
        var header = new MemoryStream();
        WriteBE(header, (uint)width);
        WriteBE(header, (uint)height);
        header.Write(new byte[] { 8, (byte)(alpha ? 6 : 2), 0, 0, 0 }, 0, 5);
        Chunk(png, "IHDR", header.ToArray());
        Chunk(png, "IDAT", zlib.ToArray());
        Chunk(png, "IEND", new byte[0]);
        return png.ToArray();
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        WriteBE(s, (uint)data.Length);
        var typed = new byte[4 + data.Length];
        for (var i = 0; i < 4; i++)
            typed[i] = (byte)type[i];
        data.CopyTo(typed, 4);
        s.Write(typed, 0, typed.Length);
        WriteBE(s, Crc(typed));
    }

    private static void WriteBE(Stream s, uint v)
    {
        s.WriteByte((byte)(v >> 24));
        s.WriteByte((byte)(v >> 16));
        s.WriteByte((byte)(v >> 8));
        s.WriteByte((byte)v);
    }

    private static uint Adler32(byte[] data)
    {
        uint a = 1, b = 0;
        foreach (var d in data)
        {
            a = (a + d) % 65521;
            b = (b + a) % 65521;
        }
        return (b << 16) | a;
    }

    private static uint[] _crcTable;

    private static uint Crc(byte[] data)
    {
        if (_crcTable == null)
        {
            _crcTable = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                var c = n;
                for (var k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                _crcTable[n] = c;
            }
        }
        var crc = 0xFFFFFFFFu;
        foreach (var d in data)
            crc = _crcTable[(crc ^ d) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }
}
