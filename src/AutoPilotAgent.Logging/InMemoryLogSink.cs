using Serilog.Core;
using Serilog.Events;

namespace AutoPilotAgent.Logging;

public sealed class InMemoryLogSink : ILogEventSink
{
    private readonly object _gate = new();
    private readonly int _maxEntries;
    private readonly Queue<LogEntry> _buffer;

    public event Action<LogEntry>? LogEmitted;

    public InMemoryLogSink(int maxEntries = 2000)
    {
        _maxEntries = maxEntries;
        _buffer = new Queue<LogEntry>(Math.Min(maxEntries, 256));
    }

    public IReadOnlyList<LogEntry> Snapshot()
    {
        lock (_gate)
        {
            return _buffer.ToList();
        }
    }

    public void Emit(LogEvent logEvent)
    {
        var entry = new LogEntry
        {
            TimestampUtc = logEvent.Timestamp.UtcDateTime,
            Level = logEvent.Level.ToString(),
            Message = logEvent.RenderMessage()
        };

        lock (_gate)
        {
            _buffer.Enqueue(entry);
            while (_buffer.Count > _maxEntries)
            {
                _buffer.Dequeue();
            }
        }

        LogEmitted?.Invoke(entry);
    }
}
