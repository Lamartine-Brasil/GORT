using System.IO;

namespace Gort.Imaging;

/// <summary>Codificador BMP 24/32bpp (tessdata/Pix.LoadFromMemory, depuração).</summary>
public static class Bmp
{
    public static byte[] Encode(byte[] pixels, int w, int h, int bpp)
    {
        int bpx = bpp / 8;
        int stride = ((w * bpx + 3) / 4) * 4;
        int data = stride * h;
        using var ms = new MemoryStream(14 + 40 + data);
        using var wr = new BinaryWriter(ms);
        wr.Write((ushort)0x4D42);
        wr.Write(14 + 40 + data);
        wr.Write((ushort)0); wr.Write((ushort)0);
        wr.Write(14 + 40);
        wr.Write(40); wr.Write(w); wr.Write(h);
        wr.Write((ushort)1); wr.Write((ushort)bpp);
        wr.Write(0); wr.Write(data);
        wr.Write(0); wr.Write(0); wr.Write(0); wr.Write(0);
        var pad = new byte[stride - w * bpx];
        for (int y = h - 1; y >= 0; y--)
        {
            wr.Write(pixels, y * w * bpx, w * bpx);
            if (pad.Length > 0) wr.Write(pad);
        }
        return ms.ToArray();
    }
}
