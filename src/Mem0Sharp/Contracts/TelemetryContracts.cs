namespace Mem0Sharp;

public interface IMemoryTelemetry
{
    Task CaptureAsync(MemoryTelemetryEvent telemetryEvent, CancellationToken cancellationToken = default);
}