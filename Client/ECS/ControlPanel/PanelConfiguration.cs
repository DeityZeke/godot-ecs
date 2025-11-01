
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
        /// Loads panel configuration from user config file, or auto-discovers panels if config doesn't exist.
        /// </summary>
        public List<PanelSettings> LoadOrDiscover()
        {
            var configPath = ProjectSettings.GlobalizePath(CONFIG_PATH);

            if (FileAccess.FileExists(CONFIG_PATH))
            {
                Logging.Logger.Log($"[PanelConfig] Loading panel config from {configPath}");
                return LoadFromConfig();
            }
            else
            {
                Logging.Logger.Log($"[PanelConfig] No config found at {configPath}, auto-discovering panels...");
                return AutoDiscoverPanels();
            }
        }

        /// <summary>
        /// Loads panel configuration from config file.
        /// </summary>
        private List<PanelSettings> LoadFromConfig()
        {
            var err = _config.Load(CONFIG_PATH);
            if (err != Error.Ok)
            {
                Logging.Logger.Log($"[PanelConfig] Failed to load config: {err}, falling back to auto-discovery", Logging.LogSeverity.Warning);
                return AutoDiscoverPanels();
            }

            var panels = new List<PanelSettings>();

            // Get all panel entries from config
            var keys = _config.GetSectionKeys(SECTION_PANELS);
            foreach (string key in keys)
            {
                if (!key.EndsWith("_order"))
                    continue;

                string panelId = key.Replace("_order", "");
                var settings = new PanelSettings
                {
                    TypeName = _config.GetValue(SECTION_PANELS, panelId + "_type", "").AsString(),
                    Order = _config.GetValue(SECTION_PANELS, panelId + "_order", 100).AsInt32(),
                    Expanded = _config.GetValue(SECTION_PANELS, panelId + "_expanded", true).AsBool(),
                    Enabled = _config.GetValue(SECTION_PANELS, panelId + "_enabled", true).AsBool()
                };

                if (!string.IsNullOrEmpty(settings.TypeName))
                {
                    panels.Add(settings);
                    _panelSettings[panelId] = settings;
                }
            }

            Logging.Logger.Log($"[PanelConfig] Loaded {panels.Count} panels from config");
            return panels.OrderBy(p => p.Order).ToList();
        }

        /// <summary>
        /// Auto-discovers panel sections using reflection.
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
                    var settings = new PanelSettings
                    {
                        TypeName = type.FullName,
                        Order = attr.DefaultOrder,
                        Expanded = attr.DefaultExpanded,
                        Enabled = true
                    };

                    panels.Add(settings);
                    Logging.Logger.Log($"[PanelConfig] Discovered panel: {type.Name} (order: {attr.DefaultOrder})");
                }
            }

            Logging.Logger.Log($"[PanelConfig] Auto-discovered {panels.Count} panels");
            return panels.OrderBy(p => p.Order).ToList();
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
