using System;
using Xunit;

namespace Gort.Tests;

/// <summary>
/// Desligamento limpo da sessão headless ao fim da coleção: sem isso o
/// dispatcher/Skia nativo morre no unload do host (0xc0000374) e a execução
/// é marcada como anulada mesmo com todos os testes verdes.
/// </summary>
[CollectionDefinition("visual")]
public sealed class VisualCollection : ICollectionFixture<HeadlessLifetime>
{
}

public sealed class HeadlessLifetime : IDisposable
{
    public void Dispose()
    {
        try
        {
            // Drena e para o loop do dispatcher antes do unload.
            HeadlessSetup.Session.Dispatch(() => { }, default).GetAwaiter().GetResult();
        }
        catch { }
        try { (HeadlessSetup.Session as IDisposable)?.Dispose(); }
        catch { }
        try { GC.Collect(); GC.WaitForPendingFinalizers(); } catch { }
    }
}
