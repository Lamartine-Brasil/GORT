using System;
using System.Threading;
using System.Threading.Tasks;
using Gort.Lifecycle;
using Gort.Loop;
using Gort.Regions;
using Gort.Store;
using Gort.UI;

namespace Gort.Translate;

/// <summary>
/// Ciclo único sobre o laço (modo pontual — RF-202): usa o mesmo
/// TranslationLoop do contínuo, no controlador compartilhado (RF-013 vale
/// entre eles). Chamado pelo botão e, na Etapa 9, pelos atalhos pontuais.
/// </summary>
public sealed class OneShotTranslator
{
    private readonly ConfigService _cfg;
    private readonly TranslationController _controller;
    private readonly RegionManager _regions;
    private readonly TranslationWindows _windows;
    private readonly TranslationPipeline _pipe;
    private readonly ILoopEffects _effects;

    public OneShotTranslator(ConfigService cfg, TranslationController controller,
        RegionManager regions, TranslationWindows windows, TranslationPipeline pipe,
        ILoopEffects? effects = null)
    {
        _cfg = cfg; _controller = controller;
        _regions = regions; _windows = windows; _pipe = pipe;
        _effects = effects ?? new NullEffects();
    }

    public async Task RunAsync(CancellationToken ct)
    {
        if (!_regions.CanTranslate(out _)) return;                 // RF-065
        // RF-085: fechar o gerenciamento precisa de UI; o chamador (botão)
        // já validou. Molduras visíveis só com ele aberto.
        _windows.ShowForMode(_cfg.Profile.WindowMode);
        var loop = new TranslationLoop(_cfg, _regions, _pipe,
            new DarkSink(_windows.Dark()), _effects);
        if (!_controller.StartLoop(loop, LoopMode.Once)) return;  // RF-013
        // Espera o término do ciclo pontual (passos de 50 ms — P-126).
        while (_controller.State != LoopState.Idle)
        {
            await Task.Delay(50, ct).ConfigureAwait(false);
        }
    }
}
