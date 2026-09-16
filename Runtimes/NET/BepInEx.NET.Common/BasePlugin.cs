using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace BepInEx.NET.Common
{
    /// <summary>Base class that every .NET plugin must inherit.</summary>
    public abstract class BasePlugin
    {
        /// <summary>Initializes a new plugin instance.</summary>
        protected BasePlugin()
        {
            var metadata = MetadataHelper.GetMetadata(this);

            HarmonyInstance = new Harmony("BepInEx.Plugin." + metadata.GUID);

            Log = Logger.CreateLogSource(metadata.Name);

            Config = new ConfigFile(Utility.CombinePaths(Paths.ConfigPath, metadata.GUID + ".cfg"), false, metadata);
        }

        /// <summary>Logger instance tied to this plugin.</summary>
        public ManualLogSource Log { get; }

        /// <summary>Default config file tied to this plugin.</summary>
        public ConfigFile Config { get; }

        /// <summary>Harmony instance tied to this plugin.</summary>
        public Harmony HarmonyInstance { get; set; }

        /// <summary>Called when the plugin is loaded.</summary>
        public abstract void Load();

        /// <summary>Called when the plugin is unloaded.</summary>
        /// <returns>True if the plugin was unloaded, otherwise false.</returns>
        public virtual bool Unload() => false;
    }
}
