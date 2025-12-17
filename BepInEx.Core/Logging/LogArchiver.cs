using System;
using System.IO;

namespace BepInEx.Logging;

/// <summary>
///     Handles archiving and cleanup of log files.
/// </summary>
public static class LogArchiver
{
    private const string LogArchiveDirectory = "Logs";

    /// <summary>
    ///     Archives all existing log files before starting a new logging session.
    /// </summary>
    /// <param name="logsRootPath">Root path where logs are stored.</param>
    /// <param name="retentionDays">Number of days to retain archived logs. 0 or negative disables archiving.</param>
    public static void ArchiveExistingLogs(string logsRootPath, int retentionDays)
    {
        if (retentionDays <= 0)
            return;

        try
        {
            var archiveDirectory = Path.Combine(logsRootPath, LogArchiveDirectory);
            Directory.CreateDirectory(archiveDirectory);

            var primaryLogPath = Path.Combine(logsRootPath, "LogOutput.log");
            TryArchiveLogFile(primaryLogPath, archiveDirectory);

            foreach (var fallbackLog in Directory.GetFiles(logsRootPath, "LogOutput.*.log"))
            {
                if (fallbackLog.EndsWith("-prev.log"))
                    continue;

                TryArchiveLogFile(fallbackLog, archiveDirectory);
            }

            CleanupOldLogs(archiveDirectory, retentionDays);
        }
        catch (Exception ex)
        {
            Logger.Log(LogLevel.Warning, $"Failed to archive logs: {ex.Message}");
        }
    }

    /// <summary>
    ///     Archives a single log file with a timestamped filename.
    /// </summary>
    /// <param name="sourceLogPath">Full path to the source log file.</param>
    /// <param name="archiveDirectory">Directory where archived logs are stored.</param>
    /// <returns>True if archiving succeeded, false otherwise.</returns>
    public static bool TryArchiveLogFile(string sourceLogPath, string archiveDirectory)
    {
        try
        {
            if (!File.Exists(sourceLogPath))
                return false;

            var fileInfo = new FileInfo(sourceLogPath);
            if (fileInfo.Length == 0)
                return false;

            var timestamp = fileInfo.LastWriteTime.ToString("yyyy-MM-dd_HH-mm-ss");
            var sourceFileName = Path.GetFileNameWithoutExtension(sourceLogPath);

            var archiveFileName = $"{sourceFileName}_{timestamp}.log";
            var archivePath = Path.Combine(archiveDirectory, archiveFileName);

            var counter = 1;
            while (File.Exists(archivePath) && counter < 100)
            {
                archiveFileName = $"{sourceFileName}_{timestamp}_{counter++}.log";
                archivePath = Path.Combine(archiveDirectory, archiveFileName);
            }

            if (counter >= 100)
            {
                Logger.Log(LogLevel.Warning, $"Too many log files with same timestamp, skipping archive of '{sourceLogPath}'");
                return false;
            }

            File.Move(sourceLogPath, archivePath);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log(LogLevel.Warning, $"Failed to archive log file '{sourceLogPath}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    ///     Deletes archived log files older than the specified retention period.
    /// </summary>
    /// <param name="archiveDirectory">Directory containing archived logs.</param>
    /// <param name="retentionDays">Number of days to retain logs. Files older than this are deleted.</param>
    public static void CleanupOldLogs(string archiveDirectory, int retentionDays)
    {
        if (retentionDays <= 0)
            return;

        try
        {
            if (!Directory.Exists(archiveDirectory))
                return;

            var cutoffDate = DateTime.Now.AddDays(-retentionDays);

            foreach (var logFile in Directory.GetFiles(archiveDirectory, "*.log"))
            {
                try
                {
                    var fileInfo = new FileInfo(logFile);
                    if (fileInfo.LastWriteTime < cutoffDate)
                        File.Delete(logFile);
                }
                catch (Exception ex)
                {
                    Logger.Log(LogLevel.Warning, $"Failed to delete old log file '{logFile}': {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log(LogLevel.Warning, $"Failed to cleanup old logs: {ex.Message}");
        }
    }
}
