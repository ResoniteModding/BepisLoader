using System;
using System.ComponentModel;
using System.IO;
using System.Text;
using BepInEx.Configuration;
using BepInEx.Core;
using BepInEx.Unix;

namespace BepInEx;

/// <summary>Manages the external console used for log output.</summary>
public static class ConsoleManager
{
    /// <summary>Hints how console output should be redirected.</summary>
    public enum ConsoleOutRedirectType
    {
        /// <summary>Lets BepInEx decide how to redirect console output.</summary>
        [Description("Auto")]
        Auto = 0,

        /// <summary>Prefers redirecting to console output; if possible, closes original standard output.</summary>
        [Description("Console Out")]
        ConsoleOut,

        /// <summary>Prefers redirecting to standard output; if possible, closes console out.</summary>
        [Description("Standard Out")]
        StandardOut
    }

    private const uint SHIFT_JIS_CP = 932;

    private const string ENABLE_CONSOLE_ARG = "--enable-console";

    /// <summary>Whether to show a console for log output.</summary>
    public static readonly ConfigEntry<bool> ConfigConsoleEnabled = ConfigFile.CoreConfig.Bind(
     "Logging.Console", "Enabled",
     false,
     "Enables showing a console for log output.");

    /// <summary>Whether closing the console is prevented in a platform-specific way.</summary>
    public static readonly ConfigEntry<bool> ConfigPreventClose = ConfigFile.CoreConfig.Bind(
     "Logging.Console", "PreventClose",
     false,
     "If enabled, will prevent closing the console (either by deleting the close button or in other platform-specific way).");

    /// <summary>Whether the console uses the Shift-JIS encoding instead of UTF-8.</summary>
    public static readonly ConfigEntry<bool> ConfigConsoleShiftJis = ConfigFile.CoreConfig.Bind(
     "Logging.Console", "ShiftJisEncoding",
     false,
     "If true, console is set to the Shift-JIS encoding, otherwise UTF-8 encoding.");

    /// <summary>Hints what handle to assign as standard output.</summary>
    public static readonly ConfigEntry<ConsoleOutRedirectType> ConfigConsoleOutRedirectType =
        ConfigFile.CoreConfig.Bind(
                                   "Logging.Console", "StandardOutType",
                                   ConsoleOutRedirectType.Auto,
                                   new StringBuilder()
                                       .AppendLine("Hints console manager on what handle to assign as StandardOut. Possible values:")
                                       .AppendLine("Auto - lets BepInEx decide how to redirect console output")
                                       .AppendLine("ConsoleOut - prefer redirecting to console output; if possible, closes original standard output")
                                       .AppendLine("StandardOut - prefer redirecting to standard output; if possible, closes console out")
                                       .ToString()
                                  );

    private static readonly bool? EnableConsoleArgOverride;

    static ConsoleManager()
    {
        // Ensure GetCommandLineArgs failing (e.g. on unix) does not kill bepin
        try
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length; i++)
            {
                var res = args[i];
                if (res == ENABLE_CONSOLE_ARG && i + 1 < args.Length && bool.TryParse(args[i + 1], out var enable))
                    EnableConsoleArgOverride = enable;
            }
        }
        catch (Exception)
        {
            // Skip
        }
    }

    /// <summary>True if the console is enabled via config or the --enable-console argument.</summary>
    public static bool ConsoleEnabled => EnableConsoleArgOverride ?? ConfigConsoleEnabled.Value;

    internal static IConsoleDriver Driver { get; set; }

    /// <summary>
    /// True if an external console has been started, false otherwise.
    /// </summary>
    public static bool ConsoleActive => Driver?.ConsoleActive ?? false;

    /// <summary>
    /// The stream that writes to the standard out stream of the process. Should never be null.
    /// </summary>
    public static TextWriter StandardOutStream => Driver?.StandardOut;

    /// <summary>
    /// The stream that writes to an external console. Null if no such console exists
    /// </summary>
    public static TextWriter ConsoleStream => Driver?.ConsoleOut;


    /// <summary>Initializes the console driver for the current platform.</summary>
    /// <param name="alreadyActive">Whether a console is already active.</param>
    /// <param name="useManagedEncoder">Whether to use the managed console encoder.</param>
    public static void Initialize(bool alreadyActive, bool useManagedEncoder)
    {
        if (PlatformUtils.Is(Platform.Unix))
            Driver = new LinuxConsoleDriver();
        else if (PlatformUtils.Is(Platform.Windows))
            Driver = new WindowsConsoleDriver();
        else
            throw new PlatformNotSupportedException("Was unable to determine console driver for platform " +
                                                    PlatformUtils.Current);

        Driver.Initialize(alreadyActive, useManagedEncoder);
    }

    private static void DriverCheck()
    {
        if (Driver == null)
            throw new InvalidOperationException("Driver has not been initialized");
    }

    /// <summary>Creates and attaches an external console.</summary>
    public static void CreateConsole()
    {
        if (ConsoleActive)
            return;

        DriverCheck();

        // Apparently some versions of Mono throw a "Encoding name 'xxx' not supported"
        // if you use Encoding.GetEncoding
        // That's why we use of codepages directly and handle then in console drivers separately
        var codepage = ConfigConsoleShiftJis.Value ? SHIFT_JIS_CP : (uint) Encoding.UTF8.CodePage;

        Driver.CreateConsole(codepage);

        if (ConfigPreventClose.Value)
            Driver.PreventClose();
    }

    /// <summary>Detaches the external console.</summary>
    public static void DetachConsole()
    {
        if (!ConsoleActive)
            return;

        DriverCheck();

        Driver.DetachConsole();
    }

    /// <summary>Sets the title of the external console.</summary>
    /// <param name="title">Title to display.</param>
    public static void SetConsoleTitle(string title)
    {
        DriverCheck();

        Driver.SetConsoleTitle(title);
    }
    /// <summary>Sets the icon of the external console.</summary>
    /// <param name="iconStream">Stream containing the icon.</param>
    public static void SetConsoleIcon(Stream iconStream)
    {
        DriverCheck();

        Driver.SetConsoleIcon(iconStream);
    }

    /// <summary>Sets the foreground color of the external console.</summary>
    /// <param name="color">Color to set.</param>
    public static void SetConsoleColor(ConsoleColor color)
    {
        DriverCheck();

        Driver.SetConsoleColor(color);
    }
}
