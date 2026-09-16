using System;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Preloader.Core.Logging;

namespace BepInEx.NET.Common
{
    /// <summary>Chainloader that loads .NET plugins.</summary>
    public class NetChainloader : BaseChainloader<BasePlugin>
    {
        // TODO: Remove once proper instance handling exists
        /// <summary>The active chainloader instance.</summary>
        public static NetChainloader Instance { get; set; }

        /// <inheritdoc />
        public override void Initialize(string gameExePath = null)
        {
            Instance = this;
            base.Initialize(gameExePath);
        }

        /// <inheritdoc />
        public override BasePlugin LoadPlugin(PluginInfo pluginInfo, Assembly pluginAssembly)
        {
            var type = pluginAssembly.GetType(pluginInfo.TypeName);

            var pluginInstance = (BasePlugin) Activator.CreateInstance(type);

            pluginInstance.Load();

            return pluginInstance;
        }

        /// <inheritdoc />
        protected override void InitializeLoggers()
        {
            base.InitializeLoggers();

            ChainloaderLogHelper.RewritePreloaderLogs();
        }
    }
}
