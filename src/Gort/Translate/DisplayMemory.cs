using System;
using System.Collections.Generic;

namespace Gort.Translate;

/// <summary>
/// Memória de exibição (RF-222..224): últimas N traduções empilhadas,
/// da mais recente à mais antiga, separadas por linha em branco dupla.
/// Expiram após P-50 s (verificação do início ao fim, para no primeiro
/// válido). Com texto vazio, só as vivas (RF-224).
/// </summary>
public sealed class DisplayMemory
{
    private readonly List<(string Text, DateTime At)> _items = new();
    private readonly Func<int> _count;
    private readonly Func<int> _seconds;

    public DisplayMemory(Func<int> count, Func<int> seconds)
    {
        _count = count; _seconds = seconds;
    }

    public string Apply(string display, DateTime now)
    {
        Sweep(now);
        if (display.Length > 0)
        {
            _items.Insert(0, (display, now));
            while (_items.Count > Math.Max(1, _count())) _items.RemoveAt(_items.Count - 1);
        }
        var parts = new List<string>();
        foreach (var (t, _) in _items) parts.Add(t);
        return string.Join("\n\n\n", parts);
    }

    private void Sweep(DateTime now)
    {
        // Lista é newest-first: expira da cauda (mais antigos).
        int life = _seconds();
        while (_items.Count > 0
            && (now - _items[^1].At).TotalSeconds > life)
            _items.RemoveAt(_items.Count - 1);
    }
}
