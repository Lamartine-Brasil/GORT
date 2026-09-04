using System;

namespace Gort.Regions;

/// <summary>Relógio injetável para testar a taxa de recálculo (RF-059).</summary>
public interface IClock
{
    DateTime UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}

public sealed class ManualClock : IClock
{
    public DateTime Now { get; set; } = new(2026, 1, 1);
    public DateTime UtcNow => Now;
    public void Advance(TimeSpan d) => Now += d;
}
