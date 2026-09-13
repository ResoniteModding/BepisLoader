using System.Reflection;
using System.Runtime.Loader;
using System.Runtime.InteropServices;

namespace BepisLoader;

public class BepisLoader
{
    internal static string resoDir = string.Empty;
    internal static AssemblyLoadContext alc = null!;
    static void Main(string[] args)
    {
        resoDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppContext.BaseDirectory;
        logPath = Path.Combine(resoDir, "BepisLoader.log");
        File.WriteAllText(logPath, string.Empty);
        Log("BepisLoader started");

        alc = new BepisLoadContext();

        // The game runs in the Default AssemblyLoadContext, not our custom BepisLoadContext. When code in the Default ALC requests a dependency, BepisLoadContext.Load() is never called, only this global AssemblyResolve event fires as a fallback.
        AppDomain.CurrentDomain.AssemblyResolve += ResolveGameDll;

        var bepinPath = Path.Combine(resoDir, "BepInEx");
        var bepinArg = Array.IndexOf(args.Select(x => x?.ToLowerInvariant()).ToArray(), "--bepinex-target");
        if (bepinArg != -1 && args.Length > bepinArg + 1)
        {
            bepinPath = args[bepinArg + 1];
        }
        Log("Loading BepInEx from " + bepinPath);

        var asm = alc.LoadFromAssemblyPath(Path.Combine(bepinPath, "core", "BepInEx.NET.CoreCLR.dll"));

        var resoDllPath = GetResoDllPath();

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
            File.WriteAllLines(Path.Combine(resoDir, "BepisCrash.log"), [DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " - Resonite crashed", e.ToString()]);
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

    private class BepisLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver? _resolver;

        public BepisLoadContext() : base(isCollectible: false)
        {
            var resoDllPath = GetResoDllPath();
            Log($"Entry assembly: {resoDllPath} (RID: {RuntimeInformation.RuntimeIdentifier})");

            if (File.Exists(resoDllPath))
                _resolver = new AssemblyDependencyResolver(resoDllPath);
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
        {
            Log($"Loading native library: {unmanagedDllName}");
            string? libraryPath = _resolver?.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (libraryPath != null)
            {
                try
                {
                    var handle = LoadUnmanagedDllFromPath(libraryPath);
                    Log(handle != IntPtr.Zero
                        ? $"Native library loaded: {unmanagedDllName} -> {libraryPath} (0x{handle:X})"
                        : $"Native library load failed: {unmanagedDllName} -> {libraryPath}");
                    return handle;
                }
                catch (Exception ex)
                {
                    Log($"Native library load threw: {unmanagedDllName} -> {libraryPath} ({ex.Message})");
                    throw;
                }
            }
            Log($"Native library unresolved by deps.json, falling back to default load: {unmanagedDllName}");
            return IntPtr.Zero;
        }
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
