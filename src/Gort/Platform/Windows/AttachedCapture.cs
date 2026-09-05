using System;
using System.Collections.Generic;
using Gort.Core;
using Gort.Imaging;

namespace Gort.Platform.Windows;

/// <summary>
/// Buffer de quadros da janela anexada (RF-093..096): anel de P-17 quadros,
/// servido de imediato quando fresco (≤ P-19); nova tentativa a cada P-20.
/// </summary>
public sealed class AttachedBuffer
{
    private readonly record struct Frame(byte[] Bytes, int W, int H, DateTime At);
    private readonly Queue<Frame> _frames = new();
    private readonly Func<DateTime> _now;
    private readonly object _gate = new();   // laço × UI (Stop)

    public AttachedBuffer(Func<DateTime>? now = null) => _now = now ?? (() => DateTime.UtcNow);

    public void Push(byte[] bytes, int w, int h)
    {
        lock (_gate)
        {
            _frames.Enqueue(new Frame(bytes, w, h, _now()));
            while (_frames.Count > Params.P17_AttachedBuffer) _frames.Dequeue();  // 🔒 5
        }
    }

    /// <summary>Último quadro válido se fresco (RF-095: P-19).</summary>
    public (byte[] Bytes, int W, int H)? Fresh()
    {
        lock (_gate)
        {
            if (_frames.Count == 0) return null;
            var arr = _frames.ToArray();
            var last = arr[^1];
            if ((_now() - last.At).TotalSeconds > Params.P19_AttachedMaxAgeSec) return null;  // 🔒 0,1
            return (last.Bytes, last.W, last.H);
        }
    }

    public int Count { get { lock (_gate) return _frames.Count; } }
    public void Clear() { lock (_gate) _frames.Clear(); }
}

/// <summary>
/// Captura anexada (RF-088 fonte 3, RF-089..097): janela específica mesmo
/// coberta (PrintWindow no cliente), origem pelos limites estendidos
/// (RF-092), parada automática ao fechar (RF-090/097). Sem borda amarela:
/// PrintWindow não desenha nenhuma (RF-091 N/A, informado na UI).
/// </summary>
public static class AttachedCapture
{
    private static nint _hwnd;
    private static string _name = "";
    private static readonly AttachedBuffer _buffer = new();
    private static int _sinceWarm;

    public static bool IsActive => _hwnd != nint.Zero;
    public static string Name => _name;

    public static void Start(nint hwnd, string name)
    {
        Stop();
        _hwnd = hwnd;
        _name = name;
        _sinceWarm = 0;
        _buffer.Clear();
    }

    public static void Stop()
    {
        _hwnd = nint.Zero;
        _name = "";
        _buffer.Clear();
    }

    public static bool IsAlive() => _hwnd != nint.Zero && Win32.IsWindow(_hwnd);  // RF-090

    /// <summary>RF-092: origem do cliente (limites estendidos com queda).</summary>
    public static ScreenRect ClientRect()
    {
        if (_hwnd == nint.Zero) return new ScreenRect(0, 0, 0, 0);
        var pt = new Win32.POINT { x = 0, y = 0 };
        if (!Win32.ClientToScreen(_hwnd, ref pt)) return new ScreenRect(0, 0, 0, 0);
        if (!Win32.GetClientRect(_hwnd, out var rc)) return new ScreenRect(0, 0, 0, 0);
        return new ScreenRect(pt.x, pt.y, rc.right - rc.left, rc.bottom - rc.top);
    }

    /// <summary>
    /// Recorta a área (global) do quadro atual; espera quadro fresco em
    /// passos de P-20 (RF-096) ou pedido de parada.
    /// </summary>
    public static RegionImage? CaptureRect(int index, ScreenRect area,
        bool needOriginal, Func<bool> stopRequested)
    {
        if (!IsAlive()) return null;                          // RF-097
        var client = ClientRect();
        if (client.W <= 0 || client.H <= 0) return null;
        // RF-094: mantém o buffer aquecido a cada P-18 quadros pedidos.
        if (_sinceWarm++ % Params.P18_AttachedWarmEvery == 0)
            Pump(client);
        while (true)
        {
            var fresh = _buffer.Fresh();
            if (fresh.HasValue) return Crop(index, area, client, fresh.Value, needOriginal);
            if (stopRequested()) return null;
            Pump(client);                                      // pede quadro novo
            if (_buffer.Fresh().HasValue) continue;
            var until = DateTime.UtcNow.AddMilliseconds(Params.P20_CaptureRetryMs);  // 🔒 2
            while (DateTime.UtcNow < until)
            {
                if (stopRequested()) return null;
                // Sleep(1) em vez de Sleep(0): acaba com o busy-spin que
                // queimava CPU girando. Estoura a espera em até ~1 quantum
                // do timer — irrelevante aqui (só no caminho de miss, onde
                // o PrintWindow já custa dezenas de ms).
                System.Threading.Thread.Sleep(1);
            }
        }
    }

    private static void Pump(ScreenRect client)
    {
        nint screenDc = Win32.GetDC(nint.Zero);
        if (screenDc == nint.Zero) return;
        try
        {
            nint memDc = Win32.CreateCompatibleDC(screenDc);
            if (memDc == nint.Zero) return;
            try
            {
                nint bmp = Win32.CreateCompatibleBitmap(screenDc, client.W, client.H);
                if (bmp == nint.Zero) return;
                try
                {
                    nint old = Win32.SelectObject(memDc, bmp);
                    try
                    {
                        if (!Win32.PrintWindow(_hwnd, memDc, Win32.PW_CLIENTONLY))
                            return;
                        var bytes = new byte[client.W * client.H * 4];
                        var info = new Win32.BITMAPINFO
                        {
                            bmiHeader =
                            {
                                biSize = (uint)System.Runtime.InteropServices.Marshal
                                    .SizeOf<Win32.BITMAPINFOHEADER>(),
                                biWidth = client.W,
                                biHeight = -client.H,
                                biPlanes = 1,
                                biBitCount = 32,
                                biCompression = Win32.BI_RGB,
                            },
                        };
                        if (Win32.GetDIBits(memDc, bmp, 0, (uint)client.H,
                                bytes, ref info, Win32.DIB_RGB_COLORS) == 0) return;
                        for (int i = 3; i < bytes.Length; i += 4) bytes[i] = 255;
                        _buffer.Push(bytes, client.W, client.H);
                    }
                    finally { Win32.SelectObject(memDc, old); }
                }
                finally { Win32.DeleteObject(bmp); }
            }
            finally { Win32.DeleteDC(memDc); }
        }
        finally { Win32.ReleaseDC(nint.Zero, screenDc); }
    }

    private static RegionImage? Crop(int index, ScreenRect area, ScreenRect client,
        (byte[] Bytes, int W, int H) frame, bool needOriginal)
    {
        // O quadro pode ser de antes de um resize: ancora na origem do
        // cliente mas limita tudo aos limites reais do quadro.
        int x1 = Math.Max(area.X, client.X), y1 = Math.Max(area.Y, client.Y);
        int x2 = Math.Min(area.X + area.W, client.X + client.W);
        int y2 = Math.Min(area.Y + area.H, client.Y + client.H);
        x2 = Math.Min(x2, client.X + frame.W);
        y2 = Math.Min(y2, client.Y + frame.H);
        int w = x2 - x1, h = y2 - y1;
        if (w <= 0 || h <= 0) return null;
        var bytes = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            int src = ((y1 - client.Y + y) * frame.W + (x1 - client.X)) * 4;
            if (src < 0 || src + w * 4 > frame.Bytes.Length) return null;
            Buffer.BlockCopy(frame.Bytes, src, bytes, y * w * 4, w * 4);
        }
        return new RegionImage
        {
            Index = index, Width = w, Height = h, Channels = 4, Bytes = bytes,
            OrigWidth = needOriginal ? w : 0, OrigHeight = needOriginal ? h : 0,
            OrigBytes = needOriginal ? (byte[])bytes.Clone() : null,
        };
    }
}
