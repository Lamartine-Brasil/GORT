using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Gort.Debug;

/// <summary>Contadores e registro de mensagens (RF-498).</summary>
public static class DebugLog
{
    private static int _ocrAttempts;
    private static int _translations;
    private static readonly List<string> _messages = new();
    private static readonly object _gate = new();

    public static int OcrAttempts => _ocrAttempts;
    public static int Translations => _translations;

    public static void OcrAttempt() => System.Threading.Interlocked.Increment(ref _ocrAttempts);
    public static void Translated() => System.Threading.Interlocked.Increment(ref _translations);

    public static void Message(string m)
    {
        lock (_gate)
        {
            _messages.Add($"[{DateTime.Now:HH:mm:ss}] {m}");
            while (_messages.Count > 200) _messages.RemoveAt(0);
        }
    }

    public static List<string> Messages()
    {
        lock (_gate) return new List<string>(_messages);
    }

    private static DateTime _lastPrune = DateTime.MinValue;

    /// <summary>
    /// Retrato por ciclo acumula 1 arquivo/ciclo: mantém os 50 mais novos
    /// (cycle-*.json, shot-*.bmp). No máximo 1 varredura a cada 5 min.
    /// </summary>
    public static void PruneDebugDir()
    {
        try
        {
            lock (_gate)
            {
                if ((DateTime.UtcNow - _lastPrune).TotalMinutes < 5) return;
                _lastPrune = DateTime.UtcNow;
            }
            var dir = new DirectoryInfo(Core.Paths.DebugDir);
            if (!dir.Exists) return;
            var old = dir.GetFiles("cycle-*").Concat(dir.GetFiles("shot-*"))
                .OrderByDescending(f => f.LastWriteTimeUtc).Skip(50);
            foreach (var f in old)
                try { f.Delete(); } catch { }
        }
        catch { }
    }
}
