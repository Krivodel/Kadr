namespace Kadr.Desktop.Services.Capture;

public sealed record CaptureMetrics(long RequestedAt, long FrozenAt, long ReadyAt, IReadOnlyList<long> PresentedAt)
{
    public double FreezeMilliseconds => System.Diagnostics.Stopwatch.GetElapsedTime(RequestedAt, FrozenAt).TotalMilliseconds;
    public double ReadbackMilliseconds => System.Diagnostics.Stopwatch.GetElapsedTime(FrozenAt, ReadyAt).TotalMilliseconds;
}
