namespace Mem0Sharp;

public sealed record MemoryTelemetryEvent(string Name, DateTimeOffset Timestamp, IReadOnlyDictionary<string, object?> Properties);