using System;
using Avalonia.Threading;
using Gort.Core;
using Gort.Platform;

namespace Gort.Regions;

/// <summary>
/// Área que segue o mouse (cap. 23, RF-454..463): centro sob o cursor,
/// reposicionada a cada P-122, recálculo no máximo a cada P-123 e só se
/// mudou. Modo compatível move a rápida/primeira (RF-460). Criação pisca
/// (RF-461); destruição desliga (RF-462); borda distinta (RF-463, na UI).
/// </summary>
public sealed class FollowMouseService
{
    private readonly RegionManager _mgr;
    private readonly Func<ScreenRect, double> _scaleOf;
    private readonly Func<bool> _compat;
    private readonly Func<(int X, int Y)> _cursor;
    private readonly DispatcherTimer? _timer;
    private DateTime _lastRecalc = DateTime.MinValue;
    private ScreenRect? _lastPos;

    public event Action? NeedsArea;     // RF-458: desenhar a dedicada
    public event Action? Blink;         // RF-461: piscar ao criar
    public AreaDef? Dedicated { get; private set; }

    public FollowMouseService(RegionManager mgr, Func<ScreenRect, double> scaleOf,
        Func<bool> compat, Func<(int X, int Y)>? cursor = null, bool startTimer = true)
    {
        _mgr = mgr;
        _scaleOf = scaleOf;
        _compat = compat;
        _cursor = cursor ?? DefaultCursor;
        _timer = null;
        if (startTimer)
        {
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(Params.P122_FollowTimerMs),  // 🔒 30
            };
            _timer.Tick += (_, _) => Tick();
        }
        // RF-552: o temporizador só roda com o modo ligado.
    }

    private static (int X, int Y) DefaultCursor()
    {
        // Cross-platform: P/Invoke Win32 só no Windows. Fora dele o
        // chamador deve injetar o cursor via Avalonia (PointerPosition);
        // aqui retornamos origem em vez de lançar DllNotFoundException.
        if (!OperatingSystem.IsWindows()) return (0, 0);
        try
        {
            var pt = new Platform.Windows.Win32.POINT();
            if (Platform.Windows.Win32.GetCursorPos(out pt)) return (pt.x, pt.y);
        }
        catch { }
        return (0, 0);
    }

    public void SetActive(bool on, bool compat)
    {
        if (on && Dedicated is null && !compat)
        {
            NeedsArea?.Invoke();   // RF-458: abre a seleção; ativa ao existir
            return;
        }
        if (on && compat && Dedicated is not null)
        {
            Dedicated = null;      // RF-460: destrói a dedicada
        }
        _mgr.FollowActive = on;
        if (on) _timer?.Start();   // RF-552: temporizador só ligado aqui
        else _timer?.Stop();
        _mgr.NotifyChanged(force: true);
    }

    public void SetArea(ScreenRect captureRect)
    {
        if (captureRect.W <= 0 || captureRect.H <= 0) return;
        Dedicated = new AreaDef { Rect = captureRect };
        Blink?.Invoke();           // RF-461
        _mgr.FollowActive = true;
        _timer?.Start();           // RF-552
        _mgr.NotifyChanged(force: true);
    }

    public void DestroyArea()
    {
        Dedicated = null;
        _mgr.FollowActive = false; // RF-462
        _timer?.Stop();
        _mgr.FollowArea = null;
        _mgr.NotifyChanged(force: true);
    }

    /// <summary>Para o temporizador (encerramento): sem novos ticks.</summary>
    public void Stop()
    {
        try { _timer?.Stop(); } catch { }
    }

    public void Tick()
    {
        if (!_mgr.FollowActive) return;
        var (cx, cy) = _cursor();
        ScreenRect target;
        if (!_compat() && Dedicated is not null)
        {
            var d = Dedicated.Rect;
            double s = _scaleOf(new ScreenRect(cx, cy, 1, 1));
            var (b, t) = FrameGeometry.Chrome(s);
            // RF-456: centro sob o cursor.
            target = new ScreenRect(
                (int)Math.Round(cx - b - d.W / 2.0),
                (int)Math.Round(cy - t - d.H / 2.0), d.W, d.H);
        }
        else
        {
            // RF-460: move a rápida ou a primeira normal.
            var q = _mgr.QuickArea;
            AreaDef? first = _mgr.Areas.Count > 0 ? _mgr.Areas[0] : null;
            if (q is null && first is null) return;
            var src = q ?? first!;
            double s = _scaleOf(new ScreenRect(cx, cy, 1, 1));
            var (b, t) = FrameGeometry.Chrome(s);
            var moved = new ScreenRect(
                (int)Math.Round(cx - b - src.Rect.W / 2.0),
                (int)Math.Round(cy - t - src.Rect.H / 2.0),
                src.Rect.W, src.Rect.H);
            src.Rect = moved;
            target = moved;
        }
        if (_lastPos == target) return;                              // só se mudou
        var now = DateTime.UtcNow;
        if ((now - _lastRecalc).TotalMilliseconds < Params.P123_FollowRecalcMs) return;  // 🔒
        _lastRecalc = now;
        _lastPos = target;
        _mgr.FollowArea = target;                                    // RF-457
        _mgr.NotifyChanged();
    }
}
