using System;
using System.Collections.Generic;

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
}
