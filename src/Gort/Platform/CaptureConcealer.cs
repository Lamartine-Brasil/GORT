using System;
using System.Collections.Generic;

namespace Gort.Platform;

/// <summary>
/// Ocultação das janelas próprias durante a captura (multiplataforma).
/// No Windows a exclusão é por afinidade (WDA_EXCLUDEFROMCAPTURE, C8);
/// nos demais SOs as janelas são momentaneamente transparentes só quando
/// intersectam as áreas — sem interseção, sem custo e sem flicker.
/// O laço usa via <see cref="PlatformFactory.CaptureConcealer"/>.
/// </summary>
public interface ICaptureConcealer
{
    IDisposable Conceal(IReadOnlyList<ScreenRect> rects);
}

/// <summary>Sem ocultação (Windows: afinidade resolve; testes).</summary>
public sealed class NullConcealer : ICaptureConcealer
{
    public static readonly NullConcealer Instance = new();
    public IDisposable Conceal(IReadOnlyList<ScreenRect> rects) => NullScope.Instance;

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
