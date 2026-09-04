using System;

namespace Gort.Loop;

/// <summary>Modo do laço: contínuo ou pontual (encerra após um ciclo — RF-202).</summary>
public enum LoopMode { Continuous, Once }

/// <summary>Contexto de um passo do laço na thread dedicada.</summary>
public sealed class LoopContext
{
    public LoopMode Mode;
    public volatile bool StopRequested;
}

/// <summary>
/// Corpo do laço: um passo (ciclo) roda por chamada, de forma síncrona, na
/// thread dedicada. Devolve false para encerrar. Nenhum ponto de espera pode
/// devolver controle antes do fim (RF-009): async é bloqueado dentro do passo.
/// </summary>
public interface ILoopBody
{
    /// <summary>Chamado na thread chamadora ao iniciar (reseta memória local).</summary>
    void Begin(LoopMode mode);
    bool Step(LoopContext ctx);
}

/// <summary>Superfície de desenho (Etapa 7: escuro; 11/12: camada/sobreposição).</summary>
public interface IDisplaySink
{
    bool IsAlive { get; }
    void Draw(string display, string recognized);
    void Repaint();
    void SetRunning(bool running);

    /// <summary>Sobreposição (Etapa 12): desenho por blocos; padrão inerte.</summary>
    void DrawOverlay(OverlayFrame frame) { }
}

/// <summary>Efeitos colaterais do ciclo (Etapas 10/16; nulos até lá).</summary>
public interface ILoopEffects
{
    /// <summary>Memória de exibição (Etapa 10): transforma o texto final.</summary>
    string ApplyDisplayMemory(string display);
    /// <summary>Área de transferência, arquivo, TTS (Etapa 16).</summary>
    void SideEffects(string display, string recognized);
}

public sealed class NullEffects : ILoopEffects
{
    public string ApplyDisplayMemory(string display) => display;
    public void SideEffects(string display, string recognized) { }
}
