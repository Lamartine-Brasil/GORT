using System;
using System.Collections.Generic;
using Gort.Core;

namespace Gort.Loop;

/// <summary>
/// Detecção de mudança entre quadros (cap. 16 🔒): compara o texto reconhecido
/// concatenado (nunca imagens — RF-192), igualdade exata pós-tratamento e
/// pré-tradução (RF-193). Diferente-ou-vazio → caminho completo (RF-194);
/// igual → sem redesenho nem efeitos (RF-195), só repintar ocioso após P-47
/// nos modos camada/sobreposição (RF-196/197). Anterior só atualiza no caminho
/// completo (RF-198); memória local ao laço (RF-199); retraduz no mesmo ciclo,
/// sem amortecimento (RF-200).
/// </summary>
public sealed class ChangeTracker
{
    private string _previous = "";   // RF-199: recomeça vazio a cada laço
    private DateTime _lastRepaint = DateTime.MinValue;

    public string Previous => _previous;

    public readonly record struct Decision(bool FullPath, bool RepaintIdle);

    public Decision Step(string current, DateTime now, bool overlayOrLayer)
    {
        if (current != _previous || current == "")       // RF-194 🔒
        {
            _previous = current;                         // RF-198
            _lastRepaint = now;
            return new Decision(FullPath: true, RepaintIdle: false);
        }
        // RF-195: igual → nada; RF-196: repintar ocioso após P-47.
        if (overlayOrLayer && (now - _lastRepaint).TotalMilliseconds >= Params.P47_IdleRepaintMs)
        {
            _lastRepaint = now;
            return new Decision(FullPath: false, RepaintIdle: true);   // RF-197: sem recalcular
        }
        return new Decision(FullPath: false, RepaintIdle: false);
    }
}

/// <summary>
/// Segunda camada de descarte (RF-203/204 🔒): por área, o último retângulo,
/// posição de cliente, texto reconhecido e traduzido. Tudo idêntico → reusa
/// o resultado anterior (preserva layout, para de tremer); só as cores
/// automáticas são substituídas. Registros de áreas ausentes são removidos.
/// Consumida pela sobreposição (Etapa 12); aqui o mecanismo + testes.
/// </summary>
public sealed class OverlayReuseCache
{
    public sealed class Record
    {
        public Platform.ScreenRect Area;
        public Platform.ScreenRect Client;
        public string Ocr = "";
        public string Translated = "";
    }

    private readonly Dictionary<int, Record> _records = new();

    /// <summary>
    /// Devolve true quando o resultado anterior pode ser reutilizado.
    /// Atualiza (ou cria) o registro com os valores novos.
    /// </summary>
    public bool ReuseOrStore(int areaIndex, Platform.ScreenRect area,
        Platform.ScreenRect client, string ocr, string translated)
    {
        if (_records.TryGetValue(areaIndex, out var r)
            && r.Area.Equals(area) && r.Client.Equals(client)
            && r.Ocr == ocr && r.Translated == translated)
            return true;
        _records[areaIndex] = new Record
        {
            Area = area, Client = client, Ocr = ocr, Translated = translated,
        };
        return false;
    }

    /// <summary>Remove registros de áreas que não vieram no ciclo (RF-204).</summary>
    public void Prune(IReadOnlySet<int> present)
    {
        var drop = new List<int>();
        foreach (var k in _records.Keys)
            if (!present.Contains(k)) drop.Add(k);
        foreach (var k in drop) _records.Remove(k);
    }

    public int Count => _records.Count;
}
