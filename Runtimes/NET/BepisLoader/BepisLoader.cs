using System.Reflection;
using System.Runtime.Loader;
using System.Runtime.InteropServices;

namespace BepisLoader;

public class BepisLoader
{
    internal static string resoDir = string.Empty;
    internal static AssemblyLoadContext alc = null!;
    internal static AssemblyDependencyResolver? resolver;
    static void Main(string[] args)
    {
        resoDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppContext.BaseDirectory;

        var bepinPath = Path.Combine(resoDir, "BepInEx");
        var bepinArg = Array.IndexOf(args.Select(x => x?.ToLowerInvariant()).ToArray(), "--bepinex-target");
        if (bepinArg != -1 && args.Length > bepinArg + 1)
        {
            bepinPath = args[bepinArg + 1];
        }

        logPath = Path.Combine(bepinPath, "EntryPoint.log");
        try
        {
            Directory.CreateDirectory(bepinPath);
            File.WriteAllText(logPath, string.Empty);
        }
        catch
        {
        }
        Log("BepisLoader started");

        // The Default ALC only probes BepisLoader.deps.json (no native entries), so resolve its native dependencies (e.g. System.Net.Quic loading libmsquic) via the game's deps.json.
        var resoDllPath = GetResoDllPath();
        if (File.Exists(resoDllPath))
            resolver = new AssemblyDependencyResolver(resoDllPath);
        AssemblyLoadContext.Default.ResolvingUnmanagedDll += ResolveDefaultUnmanagedDll;

        alc = new BepisLoadContext();

        // The game runs in the Default AssemblyLoadContext, not our custom BepisLoadContext. When code in the Default ALC requests a dependency, BepisLoadContext.Load() is never called, only this global AssemblyResolve event fires as a fallback.
        AppDomain.CurrentDomain.AssemblyResolve += ResolveGameDll;

        Log("Loading BepInEx from " + bepinPath);

        var asm = alc.LoadFromAssemblyPath(Path.Combine(bepinPath, "core", "BepInEx.NET.CoreCLR.dll"));

        var t = asm.GetType("StartupHook");
        var m = t.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static, [typeof(string), typeof(string), typeof(AssemblyLoadContext)]);
        m.Invoke(null, [resoDllPath, bepinPath, alc]);

        // Find and load Resonite
        var resoAsm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(x => x.GetName().Name == "Renderite.Host");

        try
        {
            if (resoAsm == null)
            {
                resoAsm = alc.LoadFromAssemblyPath(resoDllPath);
            }
            var result = resoAsm.EntryPoint!.Invoke(null, [args]);
            if (result is Task task) task.Wait();
        }
        catch (Exception e)
        {
            Log("Resonite crashed: " + e);
            File.WriteAllLines(Path.Combine(bepinPath, "BepisCrash.log"), [DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " - Resonite crashed", e.ToString()]);
        }
    }

    static Assembly? ResolveGameDll(object? sender, ResolveEventArgs args)
    {
        var assemblyName = new AssemblyName(args.Name);

        return ResolveInternal(assemblyName);
    }

    static Assembly? ResolveInternal(AssemblyName assemblyName)
    {
        var found = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(x => x.GetName().Name == assemblyName.Name);
        if (found != null)
        {
            return found;
        }

        var targetPath = Path.Combine(resoDir, assemblyName.Name + ".dll");
        if (File.Exists(targetPath))
        {
            var asm = alc.LoadFromAssemblyPath(targetPath);
            return asm;
        }

        return null;
    }

    private static string GetResoDllPath()
    {
        var path = Path.Combine(resoDir, "Renderite.Host.dll");
        return File.Exists(path) ? path : Path.Combine(resoDir, "Resonite.dll");
    }

    static IntPtr ResolveNativeLibrary(string libraryName, AssemblyDependencyResolver? resolver, Func<string, IntPtr> load)
    {
        Log($"Loading native library: {libraryName}");
        string? libraryPath = resolver?.ResolveUnmanagedDllToPath(libraryName);
        if (libraryPath == null)
        {
            Log($"Native library unresolved by deps.json, falling back to default load: {libraryName}");
            return IntPtr.Zero;
        }
        try
        {
            var handle = load(libraryPath);
            Log(handle != IntPtr.Zero
                ? $"Native library loaded: {libraryName} -> {libraryPath} (0x{handle:X})"
                : $"Native library load failed: {libraryName} -> {libraryPath}");
            return handle;
        }
        catch (Exception ex)
        {
            Log($"Native library load threw: {libraryName} -> {libraryPath} ({ex.Message})");
            throw;
        }
    }

    static IntPtr ResolveDefaultUnmanagedDll(Assembly _, string libraryName)
        => ResolveNativeLibrary(libraryName, resolver, NativeLibrary.Load);

    private class BepisLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver? _resolver;

        public BepisLoadContext() : base(isCollectible: false)
        {
            Log($"Entry assembly: {GetResoDllPath()} (RID: {RuntimeInformation.RuntimeIdentifier})");
            _resolver = resolver;
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            // Check already-loaded assemblies first
            var found = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(x => x.GetName().Name == assemblyName.Name);
            if (found != null) return found;

            // Use deps.json resolution
            string? assemblyPath = _resolver?.ResolveAssemblyToPath(assemblyName);
            return assemblyPath != null ? LoadFromAssemblyPath(assemblyPath) : null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
            => ResolveNativeLibrary(unmanagedDllName, _resolver, LoadUnmanagedDllFromPath);
    }

    private static string logPath = string.Empty;
    private static readonly object _lock = new object();
    private static readonly HashSet<string> _loggedMessages = new(StringComparer.OrdinalIgnoreCase);
    public static void Log(string message)
    {
        try
        {
            lock (_lock)
            {
                if (!_loggedMessages.Add(message))
                    return;
                File.AppendAllLines(logPath, [$"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [BepisLoader] {message}"]);
            }
        }
        catch
        {
        }
    }
}
