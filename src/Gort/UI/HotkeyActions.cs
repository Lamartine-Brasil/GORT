using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace Gort.UI;

/// <summary>
/// Execução das ações de atalho (22/23): roda fora do hook (RF-011) e
/// marshala UI para a thread correta. Ordem de verificação estável (RF-439).
/// </summary>
public sealed class HotkeyActions
{
    private readonly App _app;
    public HotkeyActions(App app) => _app = app;

    public void Execute(string action)
    {
        Task.Run(async () =>
        {
            try
            {
                if (action == Config.ShortcutActions.ToggleLoop) Toggle();
                else if (action == Config.ShortcutActions.Once) await OnceAsync();
                else if (action == Config.ShortcutActions.Snapshot) await SnapshotAsync();
                else if (action == Config.ShortcutActions.Quick) await QuickAsync();
                else if (action == Config.ShortcutActions.DictEditor) OpenDictEditor();
                else if (action == Config.ShortcutActions.HideWindow) HideToggle();
                else if (action == Config.ShortcutActions.FollowMouse) FollowToggle();
                else if (action.StartsWith("open-profile\t")) OpenProfile(action["open-profile\t".Length..]);
                else if (action == "forced-transparency") TransparencyToggle();
                else if (action.StartsWith("service\t")) await ServiceSwitchAsync(action["service\t".Length..]);
            }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine("GORT hotkey: " + ex.Message); }
        });
    }

    private void Toggle()
    {
        if (_app.Controller.State == Lifecycle.LoopState.Idle)
        {
            if (!_app.Regions.CanTranslate(out _)) return;
            if (!_app.EnsureOverlayOcr()) return;                // RF-351
            if (!_app.EnsureRealtimeOcr()) return;               // RF-122
            _app.ConcludeAreas();                                // RF-085
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                _app.Windows.ShowForMode(_app.Config.Profile.WindowMode);
                _app.CheckSelfCapture();
                var loop = new Loop.TranslationLoop(_app.Config, _app.Regions,
                    _app.Pipe, _app.Windows.MakeSink(), _app.LoopEffects);
                loop.Notice = msg => Dispatcher.UIThread.InvokeAsync(
                    () => _app.MainWin?.Notify(msg));                    // RF-570
                _app.CurrentLoop = loop;
                _app.Controller.StartLoop(loop, Loop.LoopMode.Continuous);
            });
        }
        else _app.Controller.RequestStopFromHook();   // RF-450: prazo curto
    }

    private async Task OnceAsync()
    {
        if (_app.Controller.State != Lifecycle.LoopState.Idle)
            _app.Controller.RequestStopFromHook();    // RF-451: pausa, prazo curto
        if (!_app.Regions.CanTranslate(out var msg))
        {
            Notify(msg);
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => _app.OpenAreas());
            return;
        }
        _app.ConcludeAreas();                         // RF-085
        await _app.OneShot.RunAsync(System.Threading.CancellationToken.None);
    }

    /// <summary>Instantâneo a partir da UI (remoto) — mesmo fluxo do atalho.</summary>
    public void SnapshotFromUi() =>
        Task.Run(async () =>
        {
            try { await SnapshotAsync(); }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine("GORT snap: " + ex.Message); }
        });

    private async Task SnapshotAsync()
    {        var sel = await _app.SelectAreaAsync();
        if (sel is null) return;
        _app.Regions.SetSnapshot(sel.Value);
        // RF-452: com captura da janela ativa, espera o foco voltar ao jogo.
        if (_app.Config.Profile.CaptureActiveWindow)
        {
            bool back = false;
            for (int i = 0; i < Core.Params.P120_ForegroundChecks; i++)   // 🔒 15
            {
                await Task.Delay(Core.Params.P121_ForegroundIntervalMs);  // 100 ms
                if (!OwnsForeground()) { back = true; break; }
            }
            if (!back)
            {
                Notify("Tempo esgotado esperando o foco voltar ao jogo; área instantânea cancelada.");
                return;
            }
        }
        await _app.OneShot.RunAsync(System.Threading.CancellationToken.None);
    }

    private bool OwnsForeground()
    {
        var fg = Platform.PlatformFactory.Current.Windows.ForegroundWindow();
        if (fg is null) return false;
        return _app.OwnHandles().Contains(fg.Value.Handle);
    }

    private async Task QuickAsync()
    {
        var sel = await _app.SelectAreaAsync();
        if (sel is not null) _app.Regions.SetQuick(sel.Value);   // RF-069
    }

    private void OpenDictEditor() =>
        Dispatcher.UIThread.InvokeAsync(() => _app.OpenDictEditor());

    private void HideToggle()
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            var dark = _app.Windows.DarkWindowOrNull();
            var layer = _app.Windows.LayerWindowOrNull();
            var over = _app.Windows.OverlayWindowOrNull();
            bool anyVisible = (dark?.IsVisible ?? false)
                || (layer?.IsVisible ?? false) || (over?.IsVisible ?? false);
            if (anyVisible) _app.Windows.HideAll();
            else _app.Windows.ShowForMode(_app.Config.Profile.WindowMode);
            if (_app.Config.Advanced.HideAlsoTranslates) Toggle();   // RF-322
        });
    }

    private void FollowToggle()
    {
        var follow = _app.Follow;
        if (follow is null) return;
        bool compat = _app.Config.Advanced.FollowCompat;
        if (_app.Regions.FollowActive) follow.SetActive(false, compat);
        else follow.SetActive(true, compat);     // RF-458: sem área, abre seleção
        Notify(_app.Regions.FollowActive
            ? "Área que segue o mouse: ligada."
            : "Área que segue o mouse: desligada.");
    }

    private void OpenProfile(string file)
    {
        if (!System.IO.File.Exists(file))
        {
            Notify($"Arquivo de perfil não encontrado: {file}");   // RF-449
            return;
        }
        _app.Config.LoadProfileIntoMain(file);
        _app.Regions.LoadFromProfile();
        Notify($"Perfil carregado: {file}");
    }

    private void TransparencyToggle()
    {
        _app.Config.Advanced.ForcedTransparency = !_app.Config.Advanced.ForcedTransparency;
        _app.Config.SaveAdvanced();
        // A janela em modo camada consome na Etapa 11.
    }

    private async Task ServiceSwitchAsync(string service)
    {
        // RF-448: ApplyChange para se rodando, aplica, salva e retoma sozinho.
        await Task.Run(() => _app.Controller.ApplyChange(() =>
        {
            _app.Config.Profile.TranslationService = service;
            _app.Config.SaveProfile();
        }, Core.Params.P03_LoopWaitMs));
        string label = Config.Catalogs.TranslationServices.FirstOrDefault(s => s.Id == service)?.DisplayPtBr ?? service;
        Notify($"Serviço de tradução: {label}");
    }

    private void Notify(string text) =>
        Dispatcher.UIThread.InvokeAsync(() => _app.MainWin?.Notify(text));
}
