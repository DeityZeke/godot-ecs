#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using UltraSim;
using UltraSim.Configuration;
using UltraSim.IO;
using UltraSim.ECS.Settings;

namespace UltraSim.ECS.Systems
{
    /// <summary>
    /// Coordinates automatic and manual world saves by selecting
    /// a registered save profile and requesting the world to persist.
    /// </summary>
    public sealed class SaveSystem : BaseSystem
    {
        public sealed class Settings : SystemSettings
        {
            public BoolSetting AutoSaveEnabled { get; }
            public FloatSetting AutoSaveIntervalSeconds { get; }
            public ChoiceStringSetting SaveProfile { get; }
            public ButtonSetting ManualSave { get; }
            public StringSetting LastSaveDisplay { get; }
            public StringSetting NextSaveDisplay { get; }

            public Settings()
            {
                AutoSaveEnabled = RegisterBool("Enable Auto-Save", true,
                    tooltip: "Automatically trigger saves on a timer.");

                AutoSaveIntervalSeconds = RegisterFloat("Auto-Save Interval (seconds)", 120f,
                    min: 5f, max: 1800f, step: 5f,
                    tooltip: "Seconds between automatic saves.");

                var defaultProfile = SaveProfileRegistry.Default?.Name ?? "Config";
                SaveProfile = RegisterChoice("Save Profile", defaultProfile,
                    GetProfileNames,
                    tooltip: "Which registered save profile to use when saving.");

                ManualSave = RegisterButton("Save Now", "Immediately trigger a world save.");

                LastSaveDisplay = RegisterString("Last Save", "Never",
                    tooltip: "Timestamp of the most recent save request.");
                NextSaveDisplay = RegisterString("Next Auto Save", "N/A",
                    tooltip: "Countdown until the next automatic save.");
            }
        }

        public Settings SystemSettings { get; } = new();
        public override SystemSettings? GetSettings() => SystemSettings;

        public override string Name => "Save System";
        public override int SystemId => typeof(SaveSystem).GetHashCode();
        public override TickRate Rate => TickRate.EveryFrame;
        public override Type[] ReadSet { get; } = Array.Empty<Type>();
        public override Type[] WriteSet { get; } = Array.Empty<Type>();

        private const string DefaultSaveFile = "world.sav";

        private double _timer;
        private DateTimeOffset? _lastSaveTime;

        public override void OnInitialize(World world)
        {
            ResetTimer();
            SystemSettings.ManualSave.Clicked += () => RequestSave(world, SaveTrigger.Manual);
            _lastSaveTime = null;
            EventSink.WorldSave += HandleWorldSave;
            EventSink.WorldLoaded += HandleWorldLoaded;
            UpdateLastSaveDisplay();
            UpdateNextSaveDisplay();
        }

        public override void OnShutdown(World world)
        {
            EventSink.WorldSave -= HandleWorldSave;
            EventSink.WorldLoaded -= HandleWorldLoaded;
        }

        public override void Update(World world, double delta)
        {
            if (!SystemSettings.AutoSaveEnabled.Value)
            {
                UpdateNextSaveDisplay();
                return;
            }

            _timer -= delta;
            UpdateNextSaveDisplay();
            if (_timer <= 0)
            {
                RequestSave(world, SaveTrigger.Auto);
            }
        }

        private void RequestSave(World world, SaveTrigger trigger)
        {
            var profile = ResolveActiveProfile(out var descriptorName);

            Logging.Log($"[SaveSystem] Triggering {trigger} save using profile '{descriptorName}'");
            EventSink.InvokeWorldSaveRequested(profile);
            _lastSaveTime = DateTimeOffset.UtcNow;
            UpdateLastSaveDisplay();
            ResetTimer();
        }

        private void ResetTimer()
        {
            _timer = Math.Max(1.0, SystemSettings.AutoSaveIntervalSeconds.Value);
            UpdateNextSaveDisplay();
        }

        private void UpdateLastSaveDisplay()
        {
            if (_lastSaveTime.HasValue)
            {
                SystemSettings.LastSaveDisplay.Value = _lastSaveTime.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            }
            else
            {
                SystemSettings.LastSaveDisplay.Value = "Never";
            }
        }

        private void UpdateNextSaveDisplay()
        {
            if (!SystemSettings.AutoSaveEnabled.Value)
            {
                SystemSettings.NextSaveDisplay.Value = "Auto disabled";
                return;
            }

            int seconds = (int)Math.Ceiling(Math.Max(0.0, _timer));
            SystemSettings.NextSaveDisplay.Value = $"{seconds}s";
        }

        public override void Serialize()
        {
            try
            {
                var path = World.Paths.GetSystemStatePath(Name);
                var config = new ConfigFile();
                config.SetValue("SaveSystem", "LastSaveTime", _lastSaveTime?.ToString("o") ?? string.Empty);
                config.Save(path);
            }
            catch (Exception ex)
            {
                Logging.Log($"[SaveSystem] Failed to serialize state: {ex.Message}", LogSeverity.Warning);
            }
        }

        public override void Deserialize()
        {
            try
            {
                var path = World.Paths.GetSystemStatePath(Name);
                if (!File.Exists(path))
                    return;

                var config = new ConfigFile();
                if (config.Load(path) != Error.Ok)
                    return;

                var stored = config.GetValue("SaveSystem", "LastSaveTime", string.Empty);
                if (!string.IsNullOrEmpty(stored) && DateTimeOffset.TryParse(stored, out var parsed))
                {
                    _lastSaveTime = parsed;
                    UpdateLastSaveDisplay();
                }
            }
            catch (Exception ex)
            {
                Logging.Log($"[SaveSystem] Failed to deserialize state: {ex.Message}", LogSeverity.Warning);
            }
        }

        private void HandleWorldSave()
        {
            _lastSaveTime = DateTimeOffset.UtcNow;
            UpdateLastSaveDisplay();
        }

        private void HandleWorldLoaded()
        {
            var world = World.Current;
            if (world?.LastSaveTime != null)
            {
                _lastSaveTime = world.LastSaveTime;
                UpdateLastSaveDisplay();
            }
        }

        internal IIOProfile ResolveActiveProfile(out string profileName)
        {
            var descriptor = SaveProfileRegistry.Get(SystemSettings.SaveProfile.Value);
            if (descriptor != null)
            {
                profileName = descriptor.Name;
                return descriptor.CreateProfile();
            }

            profileName = DefaultIOProfile.Instance.Name;
            return DefaultIOProfile.Instance;
        }

        internal string DefaultSaveFileName => DefaultSaveFile;

        private enum SaveTrigger
        {
            Auto,
            Manual
        }

        private static IReadOnlyList<string> GetProfileNames()
        {
            var descriptors = SaveProfileRegistry.Profiles;
            if (descriptors.Count == 0)
                return Array.Empty<string>();

            var names = new List<string>(descriptors.Count);
            foreach (var descriptor in descriptors)
                names.Add(descriptor.Name);
            return names;
        }
    }
}
