
#nullable enable

using System;

namespace UltraSim.Logging
{
    /// <summary>
    /// Immutable structured log entry for runtime events.
    /// </summary>
    public readonly struct LogEntry
    {
        public readonly DateTime Timestamp;
        public readonly string Message;
        public readonly LogSeverity Severity;
        public readonly string? Source;

        public LogEntry(string message, LogSeverity severity = LogSeverity.Info, string? source = null)
        {
            Timestamp = DateTime.UtcNow;
            Message = message;
            Severity = severity;
            Source = source;
        }

        public override string ToString()
            => $"[{Timestamp:HH:mm:ss}] [{Severity}] {Source ?? "ECS"}: {Message}";
    }

    public enum LogSeverity
    {
        Debug,
        Info,
        Warning,
        Error,
    }
}