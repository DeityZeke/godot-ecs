
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Godot;

namespace UltraSim.ECS
{
    /// <summary>
    /// Manages control panel section configuration, loading from config file or auto-discovery.
    /// </summary>
    public class PanelConfiguration
    {
        private const string CONFIG_PATH = "user://control_panel.cfg";
        private const string SECTION_PANELS = "Panels";

        private Godot.ConfigFile _config;
        private Dictionary<string, PanelSettings> _panelSettings = new();

        public class PanelSettings
        {
            public string TypeName { get; set; }
            public int Order { get; set; }
            public bool Expanded { get; set; }
            public bool Enabled { get; set; } = true;
        }

        public PanelConfiguration()
        {
            _config = new Godot.ConfigFile();
        }

        /// <summary>
        /// Auto-discovers panels via reflection, then merges with user config preferences.
        /// This ensures new panels always appear, and removed panels are ignored.
        /// </summary>
        public List<PanelSettings> LoadOrDiscover()
        {
            var configPath = ProjectSettings.GlobalizePath(CONFIG_PATH);

            // ALWAYS discover panels first (this finds all panels in code)
            var discoveredPanels = AutoDiscoverPanels();

            // If config exists, merge preferences (order, expanded state)
            if (FileAccess.FileExists(CONFIG_PATH))
            {
                Logging.Logger.Log($"[PanelConfig] Merging discovered panels with config from {configPath}");
                MergeConfigPreferences(discoveredPanels);
            }
            else
            {
                Logging.Logger.Log($"[PanelConfig] No config found, using discovered panel defaults");
            }

            return discoveredPanels.OrderBy(p => p.Order).ToList();
        }


        /// <summary>
        /// Auto-discovers panel sections using reflection.
        /// Returns a list of panels with default settings from attributes.
        /// </summary>
        private List<PanelSettings> AutoDiscoverPanels()
        {
            var panels = new List<PanelSettings>();
            var assembly = Assembly.GetExecutingAssembly();

            foreach (var type in assembly.GetTypes())
            {
                var attr = type.GetCustomAttribute<ControlPanelSectionAttribute>();
                if (attr != null && typeof(IControlPanelSection).IsAssignableFrom(type))
                {
                    // Create temporary instance to get panel ID
                    try
                    {
                        var tempInstance = Activator.CreateInstance(type, new object[] { null }) as IControlPanelSection;
                        if (tempInstance != null)
                        {
                            var settings = new PanelSettings
                            {
                                TypeName = type.FullName,
                                Order = attr.DefaultOrder,
                                Expanded = attr.DefaultExpanded,
                                Enabled = true
                            };

                            panels.Add(settings);
                            _panelSettings[tempInstance.Id] = settings; // Cache by ID for merging
                            Logging.Logger.Log($"[PanelConfig] Discovered panel: {type.Name} (id: {tempInstance.Id}, order: {attr.DefaultOrder})");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logging.Logger.Log($"[PanelConfig] Failed to instantiate panel {type.Name}: {ex.Message}", Logging.LogSeverity.Warning);
                    }
                }
            }

            Logging.Logger.Log($"[PanelConfig] Auto-discovered {panels.Count} panels");
            return panels;
        }

        /// <summary>
        /// Merges user config preferences (order, expanded, enabled) into discovered panels.
        /// Panels not in code are ignored. New panels get default settings.
        /// </summary>
        private void MergeConfigPreferences(List<PanelSettings> discoveredPanels)
        {
            var err = _config.Load(CONFIG_PATH);
            if (err != Error.Ok)
            {
                Logging.Logger.Log($"[PanelConfig] Failed to load config: {err}, using defaults", Logging.LogSeverity.Warning);
                return;
            }

            // Get all panel entries from config
            var keys = _config.GetSectionKeys(SECTION_PANELS);
            var configuredPanelIds = new HashSet<string>();

            foreach (string key in keys)
            {
                if (!key.EndsWith("_order"))
                    continue;

                string panelId = key.Replace("_order", "");
                configuredPanelIds.Add(panelId);

                // Find matching discovered panel
                if (_panelSettings.TryGetValue(panelId, out var settings))
                {
                    // Override discovered settings with user preferences
                    settings.Order = _config.GetValue(SECTION_PANELS, panelId + "_order", settings.Order).AsInt32();
                    settings.Expanded = _config.GetValue(SECTION_PANELS, panelId + "_expanded", settings.Expanded).AsBool();
                    settings.Enabled = _config.GetValue(SECTION_PANELS, panelId + "_enabled", settings.Enabled).AsBool();

                    Logging.Logger.Log($"[PanelConfig] Merged preferences for panel: {panelId}");
                }
                else
                {
                    // Panel in config but not in code - ignore it
                    Logging.Logger.Log($"[PanelConfig] Ignoring removed panel from config: {panelId}", Logging.LogSeverity.Warning);
                }
            }

            // Log any new panels that weren't in config
            foreach (var panel in discoveredPanels)
            {
                var tempInstance = Activator.CreateInstance(Type.GetType(panel.TypeName), new object[] { null }) as IControlPanelSection;
                if (tempInstance != null && !configuredPanelIds.Contains(tempInstance.Id))
                {
                    Logging.Logger.Log($"[PanelConfig] New panel detected (not in config): {tempInstance.Id}");
                }
            }
        }

        /// <summary>
        /// Saves current panel configuration to user config file.
        /// </summary>
        public void Save(List<IControlPanelSection> panels)
        {
            _config = new Godot.ConfigFile();

            for (int i = 0; i < panels.Count; i++)
            {
                var panel = panels[i];
                string panelId = panel.Id;

                _config.SetValue(SECTION_PANELS, panelId + "_type", panel.GetType().FullName);
                _config.SetValue(SECTION_PANELS, panelId + "_order", i);
                _config.SetValue(SECTION_PANELS, panelId + "_expanded", panel.IsExpanded);
                _config.SetValue(SECTION_PANELS, panelId + "_enabled", true);
            }

            var err = _config.Save(CONFIG_PATH);
            if (err == Error.Ok)
            {
                Logging.Logger.Log($"[PanelConfig] Saved panel config to {ProjectSettings.GlobalizePath(CONFIG_PATH)}");
            }
            else
            {
                Logging.Logger.Log($"[PanelConfig] Failed to save config: {err}", Logging.LogSeverity.Error);
            }
        }

        /// <summary>
        /// Gets stored settings for a panel ID, or null if not found.
        /// </summary>
        public PanelSettings GetSettings(string panelId)
        {
            return _panelSettings.TryGetValue(panelId, out var settings) ? settings : null;
        }
    }
}
