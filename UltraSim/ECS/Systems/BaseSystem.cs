#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

using UltraSim.IO;
using UltraSim.Configuration;
using UltraSim.ECS.Settings;
using UltraSim;

namespace UltraSim.ECS.Systems
{
    /// <summary>
    /// Performance statistics for a system (only tracked if enabled).
    /// </summary>
    public class SystemStatistics
    {
        public string SystemName = "";
        public bool IsEnabled;

        // Timing
        public double LastUpdateTimeMs;
        public double AverageUpdateTimeMs;
        public double PeakUpdateTimeMs;
        public double MinUpdateTimeMs = double.MaxValue;
        public int UpdateCount;

        // Custom metrics (systems can add their own)
        public Dictionary<string, double> CustomMetrics = new();

        // EMA smoothing factor (0.1 = 10% new value, 90% old average)
        // This gives roughly ~10-frame rolling average behavior
        private const double EMA_ALPHA = 0.1;

        public void RecordUpdate(double timeMs)
        {
            LastUpdateTimeMs = timeMs;
            PeakUpdateTimeMs = Math.Max(PeakUpdateTimeMs, timeMs);
            MinUpdateTimeMs = Math.Min(MinUpdateTimeMs, timeMs);

            // Exponential Moving Average (EMA) - responsive to changes, prevents drift
            // First update: use the actual value as baseline
            if (UpdateCount == 0)
                AverageUpdateTimeMs = timeMs;
            else
                AverageUpdateTimeMs = (EMA_ALPHA * timeMs) + ((1.0 - EMA_ALPHA) * AverageUpdateTimeMs);

            UpdateCount++;
        }

        public void SetCustomMetric(string name, double value)
        {
            CustomMetrics[name] = value;
        }

        public void Reset()
        {
            AverageUpdateTimeMs = 0;
            PeakUpdateTimeMs = 0;
            MinUpdateTimeMs = double.MaxValue;
            UpdateCount = 0;
            CustomMetrics.Clear();
        }
    }

    /// <summary>
    /// Base class for all ECS systems with OPTIONAL statistics tracking
    /// and tick-rate scheduling support.
    /// Statistics can be enabled via export parameter or debug builds.
    /// </summary>
    public abstract class BaseSystem
    {
        /// <summary>
        /// DEPRECATED: Do NOT use cached queries - they cause zombie archetype bugs.
        /// Use world.QueryArchetypes(...) directly in Update() instead.
        /// Cached queries never get invalidated when entities are destroyed,
        /// causing systems to iterate empty archetypes forever.
        /// </summary>
        [Obsolete("Cached queries cause zombie archetype bugs. Use world.QueryArchetypes(...) directly in Update() instead.")]
        protected IEnumerable<Archetype>? _cachedQuery;

        public abstract int SystemId { get; }
        public abstract string Name { get; }
        public abstract Type[] ReadSet { get; }
        public abstract Type[] WriteSet { get; }

        public virtual bool IsEnabled { get; set; }

        /// <summary>
        /// The rate at which this system should update.
        /// Override this property to change the system's update frequency.
        /// Default is EveryFrame (runs every frame).
        /// 
        /// Examples:
        ///   public override TickRate Rate => TickRate.Tick100ms;  // Runs 10x per second
        ///   public override TickRate Rate => TickRate.Manual;      // Must be explicitly invoked
        ///   public override TickRate Rate => TickRate.Tick1s;      // Runs once per second
        /// </summary>
        public virtual TickRate Rate => TickRate.EveryFrame;

        // Performance statistics (optional)
        public bool EnableStatistics = true; // Export so you can toggle in inspector
        public SystemStatistics Statistics { get; private set; } = new SystemStatistics();

#if USE_DEBUG
        // Always enable statistics in debug builds
        private Stopwatch? _updateTimer = new Stopwatch();
#else
        // Only allocate stopwatch if statistics enabled
        private Stopwatch? _updateTimer;
#endif



        #region Settings Support

        /// <summary>
        /// Override this to return your system's settings instance.
        /// Return null if your system has no settings.
        /// </summary>
        //public virtual BaseSettings? GetSettings() => null;
        public virtual SystemSettings? GetSettings() => null;
        
        //public virtual ISetting? GetSettings() => null;

        /// <summary>
        /// Saves this system's settings to disk.
        /// </summary>
        public void SaveSettings()
        {
            //if (GetSettings() is not BaseSettings settings)
            if (GetSettings() is not SystemSettings settings)
                return;

            var config = new ConfigFile();
            settings.Serialize(config, Name);

            var path = World.Paths.GetSystemSettingsPath(Name);
            var error = config.Save(path);

            if (error != Error.Ok)
                Logging.Log($"[BaseSystem] Failed to save settings for {Name}: {error}", LogSeverity.Error);
#if USE_DEBUG
    else
        Logging.Log($"[BaseSystem] Saved settings for {Name} to {path}");
#endif
        }

        /// <summary>
        /// Loads this system's settings from disk.
        /// </summary>
        public void LoadSettings()
        {
            //if (GetSettings() is not BaseSettings settings)
            if (GetSettings() is not SystemSettings settings)
                return;

            var path = World.Paths.GetSystemSettingsPath(Name);
            if (!File.Exists(path))
            {
#if USE_DEBUG
        Logging.Log($"[BaseSystem] No settings file found for {Name}, using defaults");
#endif
                return;
            }

            var config = new ConfigFile();
            var error = config.Load(path);

            if (error != Error.Ok)
            {
                Logging.Log($"[BaseSystem] Failed to load settings for {Name}: {error}", LogSeverity.Error);
                return;
            }

            settings.Deserialize(config, Name);
#if USE_DEBUG
    Logging.Log($"[BaseSystem] Loaded settings for {Name} from {path}");
#endif
        }

        #endregion



        /// <summary>
        /// Called once when the system is first added to the world.
        /// </summary>
        public virtual void OnInitialize(World world)
        {
            Statistics.SystemName = Name;

#if !USE_DEBUG
            // Allocate stopwatch only if needed (release builds)
            if (EnableStatistics && _updateTimer == null)
                _updateTimer = new Stopwatch();
#endif
        }

        /// <summary>
        /// Internal update wrapper that optionally tracks timing.
        /// ZERO OVERHEAD if statistics disabled!
        /// </summary>
        public void UpdateWithTiming(World world, double delta)
        {
            Statistics.IsEnabled = IsEnabled;

            if (!IsEnabled)
                return;

#if USE_DEBUG
            // Always track in debug builds
            _updateTimer!.Restart();
            Update(world, delta);
            _updateTimer.Stop();
            Statistics.RecordUpdate(_updateTimer.Elapsed.TotalMilliseconds);
#else
            // Only track if explicitly enabled in release builds
            if (EnableStatistics)
            {
                // Lazy allocation - create stopwatch if it doesn't exist yet
                if (_updateTimer == null)
                    _updateTimer = new Stopwatch();

                _updateTimer.Restart();
                Update(world, delta);
                _updateTimer.Stop();
                Statistics.RecordUpdate(_updateTimer.Elapsed.TotalMilliseconds);
            }
            else
            {
                // ZERO OVERHEAD PATH - Just call Update!
                Update(world, delta);
            }
#endif
        }

        /// <summary>
        /// Called when the system should update.
        /// Override this in your system implementation.
        /// 
        /// NOTE: The delta parameter represents time since the last FRAME,
        /// not time since this system last ran. Most systems should ignore delta
        /// and process all entities normally. The SystemManager handles scheduling
        /// based on the Rate property.
        /// </summary>
        public abstract void Update(World world, double delta);

        /// <summary>
        /// Called when the system is enabled.
        /// </summary>
        public virtual void Enable() { IsEnabled = true; }

        /// <summary>
        /// Called when the system is disabled.
        /// </summary>
        public virtual void Disable() { IsEnabled = false; }

        /// <summary>
        /// Called when the system is being removed from the world.
        /// </summary>
        public virtual void OnShutdown(World world) { }

        /// <summary>
        /// Optional serialization support.
        /// </summary>
        public virtual void Serialize(SystemSaveContext context) { }

        /// <summary>
        /// Optional deserialization support.
        /// </summary>
        public virtual void Deserialize(SystemLoadContext context) { }

#if USE_DEBUG
        /// <summary>
        /// Validates system state (debug builds only).
        /// </summary>
        public virtual bool IsValid => true;
#endif
    }
}
