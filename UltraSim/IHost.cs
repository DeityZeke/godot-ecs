
#nullable enable

using System;

using UltraSim;
using UltraSim.IO;

namespace UltraSim
{
    /// <summary>
    /// Defines the interface any engine or runtime host must implement to integrate UltraSim.
    /// Provides access to runtime/environment metadata and logging hooks.
    /// </summary>
    public interface IHost
    {
        /// <summary>
        /// Runtime context containing environment info, build metadata, and reflection helpers.
        /// </summary>
        RuntimeContext Runtime { get; }

        /// <summary>
        /// Host classification (server/client/hybrid).
        /// </summary>
        EnvironmentType Environment { get; }

        /// <summary>
        /// Snapshot of hardware/environment information captured at startup.
        /// </summary>
        HostEnvironment EnvironmentInfo => Runtime.Environment;

        /// <summary>
        /// Returns a handle to the root object of the engine's scene tree or context.
        /// May return null for headless/test scenarios.
        /// </summary>
        object? GetRootHandle();

        /// <summary>
        /// Receives a log entry from the UltraSim runtime.
        /// The host decides how to display, store, or forward it.
        /// </summary>
        void Log(LogEntry entry);

        /// <summary>
        /// Returns the I/O profile to use for save/load operations.
        /// Return null to use the default profile.
        /// </summary>
        IIOProfile? GetIOProfile() => null;
    }
}
