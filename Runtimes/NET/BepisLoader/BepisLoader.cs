using System.Reflection;
using System.Runtime.Loader;

namespace BepisLoader;

public class BepisLoader
{
    internal static string resoDir = string.Empty;
    internal static AssemblyLoadContext alc = null!;
    static void Main(string[] args)
    {
        resoDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
#if DEBUG
        logPath = Path.Combine(resoDir, "BepisLoader.log");
        File.WriteAllText(logPath, "BepisLoader started\n");
#endif

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
            Log("NativeLib " + unmanagedDllName);
            string? libraryPath = _resolver?.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (libraryPath != null)
            {
                Log("  Resolved: " + libraryPath);
                return LoadUnmanagedDllFromPath(libraryPath);
            }
            return IntPtr.Zero;
        }
    }

#if DEBUG
    private static string logPath;
    private static object _lock = new object();
#endif
    public static void Log(string message)
    {
#if DEBUG
        lock (_lock)
        {
            File.AppendAllLines(logPath, [message]);
        }
#endif
    }
}
