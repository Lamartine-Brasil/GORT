using System;
using System.IO;
using Gort.Imaging;

namespace Gort.Tests;

/// <summary>Artefato do teste visual da Etapa 2: grava BGRA em BMP 32bpp.</summary>
internal static class BmpWriter
{
    public static string Save(RegionImage img, string name)
    {
        var path = Path.Combine(Path.GetTempPath(), name);
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var w = new BinaryWriter(fs);
        int row = img.Width * 4, data = row * img.Height;
        w.Write((ushort)0x4D42); w.Write(14 + 40 + data); w.Write((ushort)0);
        w.Write((ushort)0); w.Write(14 + 40);
        w.Write(40); w.Write(img.Width); w.Write(img.Height);
        w.Write((ushort)1); w.Write((ushort)32); w.Write(0); w.Write(data);
        w.Write(0); w.Write(0); w.Write(0); w.Write(0);
        for (int y = img.Height - 1; y >= 0; y--)   // BMP é bottom-up
            w.Write(img.Bytes, y * row, row);
        return path;
    }
}
