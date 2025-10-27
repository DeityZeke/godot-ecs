
using System;
using System.Collections.Concurrent;

using UltraSim;

namespace UltraSim.Logging
{
    /// <summary>
    /// Thread-safe logging system. 
    /// Producers enqueue lightweight log entries from any thread.
    /// The host drains them periodically (e.g. each frame).
    /// </summary>
    public static class Logger
    {
        private static readonly ConcurrentQueue<LogEntry> _queue = new();
        private static LogSeverity _minSeverity = LogSeverity.Info;

        /// <summary>
        /// Sets a global minimum severity. Logs below this level are ignored.
        /// </summary>
        public static LogSeverity MinSeverity
        {
            get => _minSeverity;
            set => _minSeverity = value;
        }

        /// <summary>
        /// Enqueues a new log entry. Non-blocking, thread-safe.
        /// </summary>
        public static void Log(string message, LogSeverity severity = LogSeverity.Info, string? source = null)
        {
            if (severity < _minSeverity)
                return;

            _queue.Enqueue(new LogEntry(message, severity, source));
        }

        /// <summary>
        /// Attempts to dequeue one log entry.
        /// </summary>
        public static bool TryDequeue(out LogEntry entry) => _queue.TryDequeue(out entry);

        /// <summary>
        /// Drains all queued logs and forwards them to the host.
        /// </summary>
        public static void DrainToHost()
        {
            var host = SimContext.Host;
            if (host == null)
                return;

            while (_queue.TryDequeue(out var entry))
                host.Log(entry);
        }

        /// <summary>
        /// Clears any pending log entries (used during resets).
        /// </summary>
        public static void Clear() => _queue.Clear();
    }
}