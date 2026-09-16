using BepInEx.Logging;

namespace BepInEx.Preloader.Core;

/// <summary>Provides the shared log source used by the preloader.</summary>
public static class PreloaderLogger
{
    /// <summary>The preloader log source.</summary>
    public static ManualLogSource Log { get; } = Logger.CreateLogSource("Preloader");
}
