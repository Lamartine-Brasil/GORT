using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Gort.Ocr.Venv;

namespace Gort.UI;

/// <summary>
/// Instalador do motor por ambiente interpretado (RF-132..135, RF-540):
/// básica CPU, GPU com versões pré-definidas ou comando próprio, forçar
/// reinstalação, log ao vivo e fechamento bloqueado durante a instalação.
/// </summary>
public sealed class VenvInstallWindow : Window
{
    /// <summary>Bibliotecas de computação pré-definidas (dado, RF-133).</summary>
    public static readonly List<(string Name, string TorchArgs)> GpuLibs =
    [
        ("CUDA 12.1", "torch --index-url https://download.pytorch.org/whl/cu121"),
        ("CUDA 11.8", "torch --index-url https://download.pytorch.org/whl/cu118"),
    ];

    private readonly TextBox _log = new()
    {
        AcceptsReturn = true, IsReadOnly = true, Height = 260,
    };
    private readonly RadioButton _basic = new() { Content = "Básica (somente CPU)", IsChecked = true };
    private readonly RadioButton _gpu = new() { Content = "Com aceleração (GPU)" };
    private readonly RadioButton _custom = new() { Content = "Comando próprio" };
    private readonly ComboBox _gpuBox = new() { SelectedIndex = 0 };
    private readonly TextBox _customCmd = new() { PlaceholderText = "pip install ..." };
    private readonly CheckBox _force = new() { Content = "Forçar reinstalação (apaga o ambiente)" };
    private bool _installing;

    public VenvInstallWindow()
    {
        Title = "Instalar motor por ambiente interpretado";
        Width = 560; Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var gpuNames = new List<string>();
        foreach (var (n, _) in GpuLibs) gpuNames.Add(n);
        _gpuBox.ItemsSource = gpuNames;

        var install = new Button { Content = "Instalar", MinWidth = 110 };
        install.Click += (_, _) => _ = InstallAsync();
        Content = new StackPanel
        {
            Margin = new Thickness(12), Spacing = 8,
            Children =
            {
                new TextBlock { Text = "O instalador baixa o Python embutido? Não — usa o ambiente na pasta de dados e instala o pacote de OCR via pip." , TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                _basic, _gpu, _gpuBox, _custom, _customCmd, _force,
                _log, install,
            },
        };
    }

    private void Log(string s) =>
        Dispatcher.UIThread.InvokeAsync(() => _log.Text += s + "\n");

    private async Task InstallAsync()
    {
        if (_installing) return;
        if (_force.IsChecked == true && VenvEngine.LoadedThisSession)   // RF-135
        {
            Log("O ambiente está carregado nesta sessão; reinicie o programa para forçar.");
            return;
        }
        _installing = true;
        _installCts = new System.Threading.CancellationTokenSource();
        try
        {
            if (_force.IsChecked == true && Directory.Exists(VenvEngine.EnvDir))
            {
                Log("Apagando ambiente…");
                Directory.Delete(VenvEngine.EnvDir, recursive: true);
            }
            Directory.CreateDirectory(VenvEngine.EnvDir);
            string cmd, args;
            if (_custom.IsChecked == true && !string.IsNullOrWhiteSpace(_customCmd.Text))
            { cmd = "cmd.exe"; args = "/c " + _customCmd.Text; }
            else if (_gpu.IsChecked == true)
            { cmd = "cmd.exe"; args = "/c pip install " + GpuLibs[_gpuBox.SelectedIndex].TorchArgs + " easyocr"; }
            else
            { cmd = "cmd.exe"; args = "/c python -m venv . && Scripts\\pip install easyocr"; }
            // RF-132: garante venv antes do pacote quando básico/GPU.
            Log("$ " + args);
            int code;
            try { code = await RunAsync(cmd, args, _installCts.Token); }
            catch (System.OperationCanceledException) { Log("Instalação cancelada."); return; }
            catch (System.Exception ex) { Log("Falha: " + ex.Message); return; }
            Log("Saída: " + code);
            if (code == 0)
            {
                VenvEngine.EnsureScript();
                File.WriteAllText(VenvEngine.MarkerPath, "ok");
                Log("Pronto. Feche e selecione o motor.");
            }
            else Log("Falha. Veja o guia de instalação.");
        }
        finally { _installing = false; _installCts?.Dispose(); _installCts = null; }
    }

    private System.Threading.CancellationTokenSource? _installCts;

    private Task<int> RunAsync(string cmd, string args,
        System.Threading.CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<int>();
        var p = new Process
        {
            StartInfo = new ProcessStartInfo(cmd, args)
            {
                WorkingDirectory = VenvEngine.EnvDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
            EnableRaisingEvents = true,
        };
        p.OutputDataReceived += (_, e) => { if (e.Data is not null) Log(e.Data); };  // RF-134
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) Log(e.Data); };
        p.Exited += (_, _) => tcs.TrySetResult(p.ExitCode);
        // pip pode demorar: 30 min de teto; fechar a janela cancela e mata.
        var timeout = new System.Threading.CancellationTokenSource(
            TimeSpan.FromMinutes(30));
        var link = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(
            ct, timeout.Token);
        link.Token.Register(() =>
        {
            try { p.Kill(); } catch { }
            tcs.TrySetCanceled();
        });
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        return tcs.Task.ContinueWith(t =>
        {
            try { if (!p.HasExited) p.Kill(); } catch { }
            try { p.WaitForExit(2000); } catch { }
            try { p.Dispose(); } catch { }
            try { link.Dispose(); } catch { }
            try { timeout.Dispose(); } catch { }
            if (t.IsCanceled) throw new TaskCanceledException();
            return t.Result;
        });
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_installing)
        {
            // Fechar cancela a instalação em vez de travar para sempre.
            try { _installCts?.Cancel(); } catch { }
        }
        base.OnClosing(e);
    }
}
