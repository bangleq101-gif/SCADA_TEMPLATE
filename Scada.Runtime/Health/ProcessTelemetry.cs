using System.Diagnostics;

namespace Scada.Runtime.Health;

public readonly record struct ProcessTelemetryReading(
    TimeSpan? TotalProcessorTime,
    long? WorkingSetBytes);

public interface IProcessTelemetrySource
{
    ProcessTelemetryReading Read();
}

public sealed class ProcessTelemetrySource : IProcessTelemetrySource, IDisposable
{
    private readonly Process _process = Process.GetCurrentProcess();

    public ProcessTelemetryReading Read()
    {
        try
        {
            _process.Refresh();
            return new ProcessTelemetryReading(_process.TotalProcessorTime, _process.WorkingSet64);
        }
        catch (ObjectDisposedException)
        {
            return new ProcessTelemetryReading(null, null);
        }
        catch (InvalidOperationException)
        {
            return new ProcessTelemetryReading(null, null);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return new ProcessTelemetryReading(null, null);
        }
    }

    public void Dispose() => _process.Dispose();
}

public sealed record ProcessTelemetrySnapshot(
    double? CpuPercent,
    long? WorkingSetBytes,
    bool CpuAvailable)
{
    public static ProcessTelemetrySnapshot Unavailable { get; } = new(null, null, false);
}

public static class ProcessTelemetryCalculator
{
    public static double? Calculate(
        ProcessTelemetryReading? previous,
        long previousTimestamp,
        ProcessTelemetryReading current,
        long currentTimestamp,
        long timestampFrequency,
        int processorCount)
    {
        if (previous is null
            || previous.Value.TotalProcessorTime is null
            || current.TotalProcessorTime is null
            || timestampFrequency <= 0
            || processorCount <= 0
            || currentTimestamp <= previousTimestamp)
        {
            return null;
        }

        var elapsedSeconds = (currentTimestamp - previousTimestamp) / (double)timestampFrequency;
        var cpuSeconds = (current.TotalProcessorTime.Value - previous.Value.TotalProcessorTime.Value).TotalSeconds;
        if (elapsedSeconds <= 0 || cpuSeconds < 0)
        {
            return null;
        }

        return Math.Clamp(cpuSeconds / (elapsedSeconds * processorCount) * 100d, 0d, 100d);
    }
}
