using System.Buffers.Binary;
using System.IO.Compression;
using Jamm.Core.ModKit;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;

namespace Jamm.ModMaker;

internal static class SiloSprites
{
    internal static Dictionary<string, UndertaleSprite> Import(UndertaleData data, string? packageDirectory)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "jamm-silo-art-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string glowPath = ResolveOrCreateArt(packageDirectory, tempDir, "spr_jamm_silo_glow.png", CreateGlowPng);
            string diverPath = ResolveOrCreateArt(packageDirectory, tempDir, "spr_jamm_silo_diver.png", CreateDiverStripPng);
            string fishPath = ResolveOrCreateArt(packageDirectory, tempDir, "spr_jamm_silo_fish.png", CreateSanctuaryFishStripPng);

            var specs = new List<SpriteImportSpec>
            {
                new("spr_jamm_silo_glow", glowPath, 1, [64, 64], CenterOrigin: true, GenerateMask: false),
                new("spr_jamm_silo_diver", diverPath, 2, [32, 24], CenterOrigin: true, GenerateMask: true),
                new("spr_jamm_silo_fish", fishPath, 2, [24, 16], CenterOrigin: true, GenerateMask: false)
            };
            return ModKitSpriteImporter.ImportAll(data, specs);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    private static string ResolveOrCreateArt(string? packageDirectory, string tempDir, string fileName, Func<byte[]> generate)
    {
        if (!string.IsNullOrWhiteSpace(packageDirectory))
        {
            string candidate = Path.GetFullPath(Path.Combine(packageDirectory, "art", fileName));
            if (File.Exists(candidate))
                return candidate;
        }
        string fallback = Path.Combine(tempDir, fileName);
        File.WriteAllBytes(fallback, generate());
        return fallback;
    }

    private static byte[] CreateGlowPng()
    {
        const int size = 128;
        byte[] rgba = new byte[size * size * 4];
        double half = size * 0.5;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                double d = Math.Sqrt(((x - half) * (x - half)) + ((y - half) * (y - half))) / half;
                double falloff = Math.Clamp(1.0 - d, 0.0, 1.0);
                byte a = (byte)Math.Round(falloff * falloff * 255.0);
                int idx = (y * size + x) * 4;
                rgba[idx] = 255;
                rgba[idx + 1] = 255;
                rgba[idx + 2] = 255;
                rgba[idx + 3] = a;
            }
        return EncodePng(size, size, rgba);
    }

    private static byte[] CreateDiverStripPng()
    {
        const int fw = 64, h = 48, frames = 2;
        const int w = fw * frames;
        byte[] rgba = new byte[w * h * 4];
        for (int f = 0; f < frames; f++)
        {
            int ox = f * fw;
            int flipperOffset = f == 0 ? -2 : 2;
            // Cozy teal suit body + amber diving helmet + flippers
            FillEllipse(rgba, w, h, ox + 28, 26, 14, 10, 45, 165, 175, 255);
            FillEllipse(rgba, w, h, ox + 40, 22, 11, 11, 240, 185, 70, 255);
            FillEllipse(rgba, w, h, ox + 42, 22, 7, 7, 155, 235, 255, 255);
            // Cat ears inside helmet silhouette
            FillTriangle(rgba, w, h, ox + 36, 14, ox + 39, 8, ox + 42, 14, 240, 185, 70, 255);
            FillTriangle(rgba, w, h, ox + 43, 14, ox + 46, 8, ox + 49, 14, 240, 185, 70, 255);
            // Flippers
            FillEllipse(rgba, w, h, ox + 12, 24 + flipperOffset, 7, 4, 245, 140, 55, 255);
            FillEllipse(rgba, w, h, ox + 13, 30 - flipperOffset, 7, 4, 245, 140, 55, 255);
        }
        return EncodePng(w, h, rgba);
    }

    private static byte[] CreateSanctuaryFishStripPng()
    {
        const int fw = 48, h = 32, frames = 2;
        const int w = fw * frames;
        byte[] rgba = new byte[w * h * 4];
        for (int f = 0; f < frames; f++)
        {
            int ox = f * fw;
            int tailShift = f == 0 ? 0 : 2;
            FillEllipse(rgba, w, h, ox + 21, 16, 13, 8, 255, 255, 255, 255);
            FillTriangle(rgba, w, h, ox + 32, 16, ox + 44, 8 + tailShift, ox + 44, 24 - tailShift, 225, 245, 255, 255);
            FillEllipse(rgba, w, h, ox + 13, 14, 2, 2, 25, 35, 50, 255);
        }
        return EncodePng(w, h, rgba);
    }

    private static void FillEllipse(byte[] rgba, int w, int h, double cx, double cy, double rx, double ry, byte r, byte g, byte b, byte a)
    {
        for (int y = Math.Max(0, (int)(cy - ry - 1)); y <= Math.Min(h - 1, (int)(cy + ry + 1)); y++)
            for (int x = Math.Max(0, (int)(cx - rx - 1)); x <= Math.Min(w - 1, (int)(cx + rx + 1)); x++)
            {
                double dx = (x - cx) / rx, dy = (y - cy) / ry;
                if ((dx * dx) + (dy * dy) <= 1.0)
                {
                    int idx = (y * w + x) * 4;
                    rgba[idx] = r; rgba[idx + 1] = g; rgba[idx + 2] = b; rgba[idx + 3] = a;
                }
            }
    }

    private static void FillTriangle(byte[] rgba, int w, int h, int x1, int y1, int x2, int y2, int x3, int y3, byte r, byte g, byte b, byte a)
    {
        int minX = Math.Max(0, Math.Min(x1, Math.Min(x2, x3)));
        int maxX = Math.Min(w - 1, Math.Max(x1, Math.Max(x2, x3)));
        int minY = Math.Max(0, Math.Min(y1, Math.Min(y2, y3)));
        int maxY = Math.Min(h - 1, Math.Max(y1, Math.Max(y2, y3)));
        for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                int c1 = (x2 - x1) * (y - y1) - (y2 - y1) * (x - x1);
                int c2 = (x3 - x2) * (y - y2) - (y3 - y2) * (x - x2);
                int c3 = (x1 - x3) * (y - y3) - (y1 - y3) * (x - x3);
                if ((c1 >= 0 && c2 >= 0 && c3 >= 0) || (c1 <= 0 && c2 <= 0 && c3 <= 0))
                {
                    int idx = (y * w + x) * 4;
                    rgba[idx] = r; rgba[idx + 1] = g; rgba[idx + 2] = b; rgba[idx + 3] = a;
                }
            }
    }

    private static byte[] EncodePng(int width, int height, byte[] rgba)
    {
        using var ms = new MemoryStream();
        ms.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        WriteChunk(ms, "IHDR"u8, w =>
        {
            Span<byte> buf = stackalloc byte[13];
            BinaryPrimitives.WriteInt32BigEndian(buf[0..4], width);
            BinaryPrimitives.WriteInt32BigEndian(buf[4..8], height);
            buf[8] = 8;
            buf[9] = 6;
            w.Write(buf);
        });
        byte[] raw = new byte[height * (1 + width * 4)];
        for (int y = 0; y < height; y++)
            Buffer.BlockCopy(rgba, y * width * 4, raw, y * (1 + width * 4) + 1, width * 4);
        byte[] compressed;
        using (var zms = new MemoryStream())
        {
            using (var zlib = new ZLibStream(zms, CompressionLevel.Optimal, leaveOpen: true))
                zlib.Write(raw);
            compressed = zms.ToArray();
        }
        WriteChunk(ms, "IDAT"u8, w => w.Write(compressed));
        WriteChunk(ms, "IEND"u8, _ => { });
        return ms.ToArray();
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, Action<Stream> writeData)
    {
        using var body = new MemoryStream();
        body.Write(type);
        writeData(body);
        byte[] bytes = body.ToArray();
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, bytes.Length - 4);
        output.Write(len);
        output.Write(bytes);
        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(bytes));
        output.Write(crc);
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }
}
