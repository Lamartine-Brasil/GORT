using System;
using System.IO;
using System.Threading;
using Gort.Core;

namespace Gort.Lifecycle;

/// <summary>Instância única (RF-001) + marcador para desativar (RF-002).</summary>
public sealed class SingleInstance : IDisposable
{
    private Mutex? _mutex;
    private FileStream? _lockFile;

    public bool TryAcquire()
    {
        if (File.Exists(Paths.MultiInstanceMarker))
        {
            return true;   // RF-002: desativação explícita
        }
        if (OperatingSystem.IsWindows())
        {
            try
            {
                _mutex = CreateMutex();
                try { return _mutex.WaitOne(0); }   // livre: nosso; ocupado: alheio
                catch (System.Threading.AbandonedMutexException) { return true; }  // dono caiu: nosso
            }
            catch
            {
                return false;
            }
        }
        // Unix: prefixo Global\ não existe — usa lock-file em BaseDir.
        try
        {
            Paths.EnsureAll();
            _lockFile = new FileStream(Path.Combine(Paths.BaseDir, ".instance.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Global exige privilégio (usuário padrão tomava "já há instância" sem
    /// haver nenhuma): cai para Local, visível só na sessão.
    /// </summary>
    private static Mutex CreateMutex()
    {
        try { return new Mutex(false, @"Global\GORT_SingleInstance", out _); }
        catch (UnauthorizedAccessException)
        { return new Mutex(false, @"Local\GORT_SingleInstance", out _); }
    }

    public void Dispose()
    {
        _mutex?.Dispose();
        _lockFile?.Dispose();
    }
}

/// <summary>Estados do laço (RF-008): ocioso | processando | parando.</summary>
public enum LoopState { Idle, Running, Stopping }

/// <summary>
/// Controlador do laço em thread dedicada (RF-008..RF-013): o término da
/// thread é o sinal de "parou de verdade" (RF-009). Parada com prazo P-03
/// (P-04 vindo do hook — RF-011); sem reverter a flag nem executar a mudança
/// se estourar (RF-010/012); iniciar com laço vivo para o anterior (RF-013).
/// Erros do laço são reportados por evento para a thread de UI (RF-014).
/// </summary>
public sealed class TranslationController
{
    private readonly object _gate = new();
    private Thread? _thread;
    private Loop.LoopContext? _ctx;
    private Loop.ILoopBody? _body;
    private Loop.LoopMode _mode;
    private int _generation;
    public LoopState State { get; private set; } = LoopState.Idle;

    /// <summary>RF-014: erro não tratado do laço (exibir pela UI, sem modal).</summary>
    public event Action<Exception>? LoopError;

    /// <summary>Fim do laço (gravação da memória — Etapa 10).</summary>
    public event Action? LoopEnded;

    /// <summary>
    /// Inicia o corpo na thread do laço. Com laço vivo, para o anterior
    /// primeiro (P-03); se não parar, não inicia (RF-013).
    /// </summary>
    public bool StartLoop(Loop.ILoopBody body, Loop.LoopMode mode)
    {
        lock (_gate)
        {
            if (State != LoopState.Idle && !StopLocked(Core.Params.P03_LoopWaitMs))
                return false;
            _body = body;
            _mode = mode;
            _ctx = new Loop.LoopContext { Mode = mode };
            _generation++;
            int gen = _generation;
            body.Begin(mode);   // reseta memória local do laço (RF-199)
            State = LoopState.Running;
            _thread = new Thread(() => ThreadMain(gen))
            {
                IsBackground = true,
                Name = "GORT-loop",
            };
            _thread.Start();
            return true;
        }
    }

    public bool RequestStop(int timeoutMs)
    {
        lock (_gate) return StopLocked(timeoutMs);
    }

    /// <summary>RF-011: parada vinda do interceptador usa o prazo curto.</summary>
    public bool RequestStopFromHook() => RequestStop(Core.Params.P04_HookWaitMs);

    private bool StopLocked(int timeoutMs)
    {
        if (State == LoopState.Idle) return true;
        State = LoopState.Stopping;
        if (_ctx is not null) _ctx.StopRequested = true;
        var t = _thread;
        // Espera fora do lock para não travar observadores do estado.
        Monitor.Exit(_gate);
        try { return t is null || t.Join(timeoutMs); }
        finally { Monitor.Enter(_gate); }
        // Se estourou: flag permanece, mudança cancelada (RF-010).
    }

    /// <summary>
    /// RF-012: pausar → aplicar → retomar. Se a parada estourar, nada é
    /// aplicado e devolve false.
    /// </summary>
    public bool ApplyChange(Action change, int timeoutMs)
    {
        Loop.ILoopBody? body;
        Loop.LoopMode mode;
        lock (_gate)
        {
            if (State == LoopState.Idle)
            {
                Monitor.Exit(_gate);
                try { change(); }
                finally { Monitor.Enter(_gate); }
                return true;
            }
            body = _body; mode = _mode;
            if (!StopLocked(timeoutMs)) return false;   // aborta, informa
        }
        try { change(); }
        catch { StartLoop(body!, mode); throw; }
        return StartLoop(body!, mode);
    }

    private void ThreadMain(int gen)
    {
        Loop.LoopContext ctx;
        Loop.ILoopBody body;
        lock (_gate) { ctx = _ctx!; body = _body!; }
        try
        {
            while (!ctx.StopRequested)
            {
                bool more;
                try { more = body.Step(ctx); }
                catch (Exception ex)
                {
                    try { LoopError?.Invoke(ex); } catch { }
                    break;                              // RF-014: termina limpo
                }
                if (!more) break;
                if (ctx.Mode == Loop.LoopMode.Once) break;   // RF-202: pontual encerra
            }
        }
        finally
        {
            bool mine = false;
            lock (_gate)
            {
                if (gen == _generation)
                {
                    State = LoopState.Idle;
                    _thread = null;
                    mine = true;
                }
            }
            if (mine)
            {
                try { LoopEnded?.Invoke(); } catch { }
            }
        }
    }
}
