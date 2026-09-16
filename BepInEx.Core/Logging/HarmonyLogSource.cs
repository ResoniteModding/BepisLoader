using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLogger = HarmonyLib.Tools.Logger;

namespace BepInEx.Logging;

/// <summary>Log source that forwards HarmonyX messages to the BepInEx log.</summary>
public class HarmonyLogSource : ILogSource
{
    private static readonly ConfigEntry<HarmonyLogger.LogChannel> LogChannels = ConfigFile.CoreConfig.Bind(
     "Harmony.Logger",
     "LogChannels",
     HarmonyLogger.LogChannel.Warn | HarmonyLogger.LogChannel.Error,
     "Specifies which Harmony log channels to listen to.\nNOTE: IL channel dumps the whole patch methods, use only when needed!");

    private static readonly Dictionary<HarmonyLogger.LogChannel, LogLevel> LevelMap = new()
    {
        [HarmonyLogger.LogChannel.Info] = LogLevel.Info,
        [HarmonyLogger.LogChannel.Warn] = LogLevel.Warning,
        [HarmonyLogger.LogChannel.Error] = LogLevel.Error,
        [HarmonyLogger.LogChannel.IL] = LogLevel.Debug
    };

    /// <summary>Creates a new Harmony log source and subscribes to HarmonyX log messages.</summary>
    public HarmonyLogSource()
    {
        HarmonyLogger.ChannelFilter = LogChannels.Value;
        HarmonyLogger.MessageReceived += HandleHarmonyMessage;
    }

    /// <inheritdoc />
    public void Dispose() => HarmonyLogger.MessageReceived -= HandleHarmonyMessage;

    /// <summary>Name of the log source.</summary>
    public string SourceName { get; } = "HarmonyX";
    /// <summary>Occurs when HarmonyX produces a log message.</summary>
    public event EventHandler<LogEventArgs> LogEvent;

    private void HandleHarmonyMessage(object sender, HarmonyLogger.LogEventArgs e)
    {
        if (!LevelMap.TryGetValue(e.LogChannel, out var level))
            return;

        LogEvent?.Invoke(this, new LogEventArgs(e.Message, level, this));
    }
}
