using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Gort.Translate;

/// <summary>
/// Enquadramento do canal (RF-287): 2 bytes de comprimento (alto, baixo),
/// conteúdo UTF-16, truncado em 65535 bytes.
/// </summary>
public static class PipeFraming
{
    public const int MaxBytes = 65535;   // P-135

    public static byte[] Encode(string text)
    {
        byte[] body = Encoding.Unicode.GetBytes(text);
        if (body.Length > MaxBytes)
            body = body[..MaxBytes];                                  // RF-287
        var msg = new byte[2 + body.Length];
        msg[0] = (byte)(body.Length >> 8);
        msg[1] = (byte)(body.Length & 255);
        body.CopyTo(msg, 2);
        return msg;
    }

    public static async Task<string> ReadAsync(Stream s, CancellationToken ct)
    {
        var len = new byte[2];
        await FillAsync(s, len, ct).ConfigureAwait(false);
        int n = (len[0] << 8) | len[1];
        var body = new byte[n];
        await FillAsync(s, body, ct).ConfigureAwait(false);
        return Encoding.Unicode.GetString(body);
    }

    private static async Task FillAsync(Stream s, byte[] buf, CancellationToken ct)
    {
        int off = 0;
        while (off < buf.Length)
        {
            int r = await s.ReadAsync(buf.AsMemory(off), ct).ConfigureAwait(false);
            if (r == 0) throw new EndOfStreamException();
            off += r;
        }
    }
}

/// <summary>
/// Tradutor local por processo auxiliar (RF-284..291): inicia o ajudante,
/// handshake de inicialização (250 ms — P-138), comandos `comando,dados`.
/// Biblioteca via subpasta ou registro (RF-288); sem ela, o serviço some
/// da lista (RF-574). UTF-16 preferido (RF-289).
/// </summary>
public sealed class LocalWorker : HttpTranslator, IDisposable
{
    public override string Id => "local-worker";
    public override string Display => "Tradutor local por processo auxiliar";
    public override string DefaultToken => RemoteDefaults.DefaultToken;
    public override bool UsesResultMemory => false;   // RF-214

    /// <summary>Bibliotecas candidatas (dado, RF-288).</summary>
    public static readonly string[] LibNames = ["GortTranslator.dll", "translator.dll"];
    public const string RegPath = @"HKEY_CURRENT_USER\SOFTWARE\GORT\Translator";

    private Process? _child;
    private NamedPipeClientStream? _pipe;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool? _handshake;

    public LocalWorker(HttpMessageHandler? handler = null) : base(handler, 5000) { }

    public static string? FindLibrary()
    {
        string libDir = Path.Combine(AppContext.BaseDirectory, "lib", "translator");
        foreach (var n in LibNames)
        {
            string p = Path.Combine(libDir, n);
            if (File.Exists(p)) return p;
        }
        // Windows: registro HKCU. Fora do Windows: arquivo de texto com o
        // caminho em BaseDir (~/.config/gort ou ~/Library), sem depender do Registry.
        if (OperatingSystem.IsWindows())
        {
            try
            {
                string? reg = Registry.GetValue(RegPath, "Path", null) as string;
                if (reg is not null && File.Exists(reg)) return reg;
            }
            catch { }
        }
        else
        {
            try
            {
                string f = Path.Combine(Core.Paths.BaseDir, "translator.path");
                if (File.Exists(f))
                {
                    string p = File.ReadAllText(f).Trim();
                    if (p.Length > 0 && File.Exists(p)) return p;
                }
            }
            catch { }
        }
        return null;
    }

    public override async Task<ServiceResult> TranslateAsync(IReadOnlyList<string> texts,
        string srcCode, string dstCode, CancellationToken ct)
    {
        if (FindLibrary() is null)
            return Fail("Processo auxiliar indisponível.");   // RF-290
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!await EnsureReadyAsync(ct).ConfigureAwait(false))
                return Fail("Processo auxiliar indisponível.");
            string payload = texts.Count > 0 ? texts[0] : "";
            string reply = await CommandAsync(
                $"tr,{srcCode},{dstCode},{payload}", ct)
                .ConfigureAwait(false);
            if (reply.StartsWith("ok,", StringComparison.Ordinal))
                return new ServiceResult { Translations = new List<string> { reply[3..] } };
            if (reply.StartsWith("err,", StringComparison.Ordinal))
                return Fail(reply[4..]);
            return Fail("Resposta inesperada do processo auxiliar.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return Fail(ex.Message); }
        finally { _gate.Release(); }
    }

    private async Task<bool> EnsureReadyAsync(CancellationToken ct)
    {
        if (_handshake == true && _pipe is not null && _pipe.IsConnected) return true;
        _handshake = null;
        try { _child?.Kill(); } catch { }                        // RF-285
        string pipeName = "gort-tr-" + Environment.ProcessId;
        string? lib = FindLibrary();
        _child = new Process
        {
            StartInfo = new ProcessStartInfo(
                Environment.ProcessPath ?? "Gort",
                $"--translate-worker \"{pipeName}\" \"{lib}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        if (!_child.Start()) return false;
        _pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
        var until = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < until)                          // RF-286: verificação
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await _pipe.ConnectAsync(250, ct).ConfigureAwait(false);
                break;
            }
            catch (TimeoutException) { }
        }
        if (!_pipe.IsConnected) return false;
        string id = await PipeFraming.ReadAsync(_pipe, ct).ConfigureAwait(false);
        if (!id.StartsWith("GORT-TR,", StringComparison.Ordinal)) return false;
        string reply = await CommandAsync("init,", ct).ConfigureAwait(false);
        _handshake = reply.StartsWith("ok,", StringComparison.Ordinal);
        return _handshake == true;
    }

    private async Task<string> CommandAsync(string cmd, CancellationToken ct)
    {
        byte[] msg = PipeFraming.Encode(cmd);
        await _pipe!.WriteAsync(msg, ct).ConfigureAwait(false);
        await _pipe.FlushAsync(ct).ConfigureAwait(false);
        using var timeout = new CancellationTokenSource(5000);   // P-143
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        return await PipeFraming.ReadAsync(_pipe, linked.Token).ConfigureAwait(false);
    }

    public void Dispose()   // RF-016/285: encerra o auxiliar
    {
        try { _child?.Kill(); } catch { }
        try { _pipe?.Dispose(); } catch { }
        _child = null;
        _pipe = null;
        _handshake = null;
    }

    /// <summary>
    /// Lado ajudante (`GORT --translate-worker pipe lib`): identifica,
    /// responde init e traduz. Sem ABI da biblioteca proprietária, informa
    /// indisponibilidade sem quebrar (RF-290).
    /// </summary>
    public static async Task<int> RunWorkerAsync(string pipeName, string lib)
    {
        using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1);
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(30));
        try
        {
            await server.WaitForConnectionAsync(cts.Token).ConfigureAwait(false);
            await server.WriteAsync(PipeFraming.Encode("GORT-TR,1.0"), cts.Token)
                .ConfigureAwait(false);
            await server.FlushAsync(cts.Token).ConfigureAwait(false);
            bool hasLib = File.Exists(lib);
            while (server.IsConnected)
            {
                string cmd;
                try { cmd = await PipeFraming.ReadAsync(server, cts.Token).ConfigureAwait(false); }
                catch { break; }
                string reply;
                if (cmd.StartsWith("init,", StringComparison.Ordinal))
                    reply = hasLib ? "ok," : "fail,engine-missing";
                else if (cmd.StartsWith("tr,", StringComparison.Ordinal))
                    reply = hasLib ? "err,not-implemented" : "err,engine-missing";
                else reply = "err,unknown-command";
                try
                {
                    await server.WriteAsync(PipeFraming.Encode(reply), cts.Token)
                        .ConfigureAwait(false);
                    await server.FlushAsync(cts.Token).ConfigureAwait(false);
                }
                catch { break; }
            }
            return 0;
        }
        catch { return 1; }
    }
}
