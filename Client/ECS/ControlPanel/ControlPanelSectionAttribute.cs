
using System;

namespace Client.ECS.ControlPanel
{
    /// <summary>
    /// Marks a class as a control panel section for auto-discovery.
    /// Used as fallback when no config file exists.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class ControlPanelSectionAttribute : Attribute
    {
        /// <summary>
        /// Default display order (lower = higher in list).
        /// Can be overridden by user config.
        /// </summary>
        public int DefaultOrder { get; }

        /// <summary>
        /// Whether this panel is expanded by default.
        /// Can be overridden by user config.
        /// </summary>
        public bool DefaultExpanded { get; }

        public ControlPanelSectionAttribute(int defaultOrder = 100, bool defaultExpanded = true)
        {
            DefaultOrder = defaultOrder;
            DefaultExpanded = defaultExpanded;
        }
    }
}
