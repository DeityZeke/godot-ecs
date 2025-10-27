
#nullable enable

using System;

using UltraSim.Logging;

namespace UltraSim
{
    /// <summary>
    /// Global context that connects UltraSim subsystems to their host environment.
    /// </summary>
    public static class SimContext
    {
        /// <summary>
        /// Reference to the host environment (e.g., Godot, dedicated server, etc.)
        /// </summary>
        public static IHost? Host { get; private set; }

        /// <summary>
        /// Initializes UltraSim with the given host.
        /// </summary>
        public static void Initialize(IHost host)
        {
            Host = host;
            Logger.Log($"SimContext initialized with host: {host.GetType().Name}",
                LogSeverity.Debug, nameof(SimContext));
        }

        /// <summary>
        /// Clears the current host reference.
        /// </summary>
        public static void Clear() => Host = null;
    }
}