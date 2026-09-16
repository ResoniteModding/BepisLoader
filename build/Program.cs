// ReSharper disable ClassNeverInstantiated.Global
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using Cake.Common;
using Cake.Common.IO;
using Cake.Common.Tools.DotNet;
using Cake.Common.Tools.DotNet.Build;
using Cake.Common.Tools.DotNet.NuGet.Push;
using Cake.Common.Xml;
using Cake.Core;
using Cake.Core.Diagnostics;
using Cake.Core.IO;
using Cake.Frosting;
using Cake.Json;
using Microsoft.Build.Definition;
using Microsoft.Build.Evaluation;

/// <summary>Cake Frosting build entry point.</summary>
public static class Program
{
    /// <summary>Runs the build with the given command-line arguments.</summary>
    /// <param name="args">Command-line arguments passed to the build.</param>
    /// <returns>The process exit code.</returns>
    public static int Main(string[] args)
    {
        return new CakeHost()
               .UseContext<BuildContext>()
               .Run(args);
    }
}

/// <summary>Shared state and settings for the build.</summary>
public class BuildContext : FrostingContext
{
    /// <summary>Kind of build to produce.</summary>
    public enum ProjectBuildType
    {
        /// <summary>Stable release build.</summary>
        Release,
        /// <summary>Local development build.</summary>
        Development,
        /// <summary>Bleeding-edge CI build.</summary>
        BleedingEdge
    }

    /// <summary>Version of hookfxr downloaded for distributions.</summary>
    public const string HOOKFXR_VERSION = "1.1.0";

    internal readonly DistributionTarget[] Distributions =
    {
        //new("NET.Framework", "win-x86", "net40"),
        //new("NET.Framework", "win-x86", "net452"),
        //new("NET.CoreCLR", "win-x64", "netcoreapp3.1"),
        //new("NET.CoreCLR", "win-x64", "net9.0"),
        new("NET", "BepisLoader", "win-x64", "net10.0"),
        // new("NET", "BepisLoader", "linux-x64", "net10.0")
    };


    /// <summary>Creates build state from the Cake context.</summary>
    /// <param name="ctx">Cake context to derive paths and arguments from.</param>
    public BuildContext(ICakeContext ctx)
        : base(ctx)
    {
        RootDirectory = ctx.Environment.WorkingDirectory.GetParent();
        OutputDirectory = RootDirectory.Combine("bin");
        CacheDirectory = OutputDirectory.Combine(".dep_cache");
        DistributionDirectory = OutputDirectory.Combine("dist");
        var props = Project.FromFile(RootDirectory.CombineWithFilePath("Directory.Build.props").FullPath,
                                     new ProjectOptions());
        VersionPrefix = props.GetPropertyValue("VersionPrefix");
        CurrentCommit = ctx.GitLogTip(RootDirectory);

        BuildType = ctx.Argument("build-type", ProjectBuildType.Development);
        BuildId = ctx.Argument("build-id", -1);
        LastBuildCommit = ctx.Argument("last-build-commit", "");
        NugetApiKey = ctx.Argument("nuget-api-key", "");
        NugetSource = ctx.Argument("nuget-source", "https://nuget.bepinex.dev/v3/index.json");
    }

    /// <summary>Kind of build to produce.</summary>
    public ProjectBuildType BuildType { get; }
    /// <summary>CI build identifier. Negative when not built by CI.</summary>
    public int BuildId { get; }
    /// <summary>Commit the last build ran on. Empty when building everything.</summary>
    public string LastBuildCommit { get; }
    /// <summary>API key used to push NuGet packages.</summary>
    public string NugetApiKey { get; }
    /// <summary>NuGet feed packages are pushed to.</summary>
    public string NugetSource { get; }

    /// <summary>Repository root directory.</summary>
    public DirectoryPath RootDirectory { get; }
    /// <summary>Directory holding all build output, including final distributions.</summary>
    public DirectoryPath OutputDirectory { get; }
    /// <summary>Directory holding cached downloads.</summary>
    public DirectoryPath CacheDirectory { get; }
    /// <summary>Directory holding assembled distributions.</summary>
    public DirectoryPath DistributionDirectory { get; }

    /// <summary>Version prefix read from the repository properties.</summary>
    public string VersionPrefix { get; }
    /// <summary>Commit the current build is produced from.</summary>
    public GitCommit CurrentCommit { get; }

    /// <summary>Version suffix for the current build type.</summary>
    public string VersionSuffix => BuildType switch
    {
        ProjectBuildType.Release      => "",
        ProjectBuildType.Development  => "dev",
        ProjectBuildType.BleedingEdge => $"be.{BuildId}",
        var _                         => throw new ArgumentOutOfRangeException()
    };

    /// <summary>Full package version of the current build.</summary>
    public string BuildPackageVersion =>
        VersionPrefix + BuildType switch
        {
            ProjectBuildType.Release => "",
            var _                    => $"-{VersionSuffix}+{this.GitShortenSha(RootDirectory, CurrentCommit)}",
        };

    /// <summary>Download URL of the hookfxr release zip.</summary>
    public static string HookfxrZipUrl = $"https://github.com/ResoniteModding/hookfxr/releases/download/v{HOOKFXR_VERSION}/hookfxr-Release.zip";
}

/// <summary>Cleans build output directories.</summary>
[TaskName("Clean")]
public sealed class CleanTask : FrostingTask<BuildContext>
{
    /// <inheritdoc/>
    public override void Run(BuildContext ctx)
    {
        ctx.CreateDirectory(ctx.OutputDirectory);
        ctx.CleanDirectory(ctx.OutputDirectory,
                           f => !f.Path.FullPath.Contains(".dep_cache"));

        ctx.Log.Information("Cleaning up old build objects");
        ctx.CleanDirectories(ctx.RootDirectory.Combine("**/BepInEx.*/**/bin").FullPath);
        ctx.CleanDirectories(ctx.RootDirectory.Combine("**/BepInEx.*/**/obj").FullPath);
    }
}

/// <summary>Restores dotnet CLI tools.</summary>
[TaskName("RestoreTools")]
[IsDependentOn(typeof(CleanTask))]
public sealed class RestoreToolsTask : FrostingTask<BuildContext>
{
    /// <inheritdoc/>
    public override void Run(BuildContext ctx)
    {
        ctx.Log.Information("Restoring dotnet tools...");

        ctx.DotNetToolRestore(new Cake.Common.Tools.DotNet.Tool.DotNetToolRestoreSettings
        {
            WorkingDirectory = ctx.RootDirectory
        });

        ctx.Log.Information("Dotnet tools restored successfully.");
    }
}

/// <summary>Builds the solution and publishes the loader.</summary>
[TaskName("Compile")]
[IsDependentOn(typeof(RestoreToolsTask))]
public sealed class CompileTask : FrostingTask<BuildContext>
{
    /// <inheritdoc/>
    public override void Run(BuildContext ctx)
    {
        var hasBepisLoader = ctx.Distributions.Any(d => d.Runtime == "BepisLoader");

        var buildSettings = new DotNetBuildSettings
        {
            Configuration = "Release"
        };
        if (ctx.BuildType != BuildContext.ProjectBuildType.Release)
        {
            buildSettings.MSBuildSettings = new()
            {
                VersionSuffix = ctx.VersionSuffix,
                Properties =
                {
                    ["SourceRevisionId"] = new[] { ctx.CurrentCommit.Sha },
                    ["RepositoryBranch"] = new[] { ctx.GitBranchCurrent(ctx.RootDirectory).FriendlyName },
                    ["DebugType"] = new[] { "embedded" },
                    ["DebugSymbols"] = new[] { "true" }
                }
            };
        }

        ctx.DotNetBuild(ctx.RootDirectory.CombineWithFilePath("BepInEx.slnx").FullPath, buildSettings);

        if (hasBepisLoader)
        {
            ctx.Log.Information("Publishing BepisLoader...");

            var bepisLoaderDist = ctx.Distributions.First(d => d.Runtime == "BepisLoader");
            var publishSettings = new Cake.Common.Tools.DotNet.Publish.DotNetPublishSettings
            {
                Configuration = "Release",
                Framework = bepisLoaderDist.FrameworkTarget,
                OutputDirectory = ctx.OutputDirectory.Combine("BepisLoader").Combine(bepisLoaderDist.FrameworkTarget),
                PublishSingleFile = false,
                PublishTrimmed = false
            };

            if (ctx.BuildType != BuildContext.ProjectBuildType.Release)
            {
                publishSettings.MSBuildSettings = new()
                {
                    VersionSuffix = ctx.VersionSuffix,
                    Properties =
                    {
                        ["SourceRevisionId"] = new[] { ctx.CurrentCommit.Sha },
                        ["RepositoryBranch"] = new[] { ctx.GitBranchCurrent(ctx.RootDirectory).FriendlyName },
                        ["DebugType"] = new[] { "embedded" },
                        ["DebugSymbols"] = new[] { "true" },
                        ["GeneratePackageOnBuild"] = new[] { "false" }
                    }
                };
            }
            else
            {
                publishSettings.MSBuildSettings = new()
                {
                    Properties =
                    {
                        ["GeneratePackageOnBuild"] = new[] { "false" }
                    }
                };
            }

            ctx.DotNetPublish(ctx.RootDirectory.Combine("Runtimes/NET/BepisLoader/BepisLoader.csproj").FullPath, publishSettings);
        }
    }
}

/// <summary>Downloads external distribution dependencies.</summary>
[TaskName("DownloadDependencies")]
public sealed class DownloadDependenciesTask : FrostingTask<BuildContext>
{
    /// <inheritdoc/>
    public override void Run(BuildContext ctx)
    {
        ctx.Log.Information("Downloading dependencies");
        ctx.CreateDirectory(ctx.CacheDirectory);

        var cache = new DependencyCache(ctx, ctx.CacheDirectory.CombineWithFilePath("cache.json"));

        cache.Refresh("ResoniteModding/hookfxr", BuildContext.HOOKFXR_VERSION, () =>
        {
            ctx.Log.Information($"Downloading hookfxr {BuildContext.HOOKFXR_VERSION}");
            var hookfxrDir = ctx.CacheDirectory.Combine("hookfxr");
            ctx.CreateDirectory(hookfxrDir);
            ctx.CleanDirectory(hookfxrDir);
            ctx.DownloadZipFiles($"hookfxr {BuildContext.HOOKFXR_VERSION}",
                                 ("hookfxr", BuildContext.HookfxrZipUrl, hookfxrDir));
        });

        cache.Save();
    }
}

/// <summary>Assembles distributable archives from build output.</summary>
[TaskName("MakeDist")]
[IsDependentOn(typeof(CompileTask))]
[IsDependentOn(typeof(DownloadDependenciesTask))]
public sealed class MakeDistTask : FrostingTask<BuildContext>
{
    /// <inheritdoc/>
    public override void Run(BuildContext ctx)
    {
        ctx.CreateDirectory(ctx.DistributionDirectory);
        ctx.CleanDirectory(ctx.DistributionDirectory);

        foreach (var dist in ctx.Distributions)
        {
            ctx.Log.Information($"Creating distribution {dist.Target}");
            var targetDir = ctx.DistributionDirectory.Combine(dist.Target);
            ctx.CreateDirectory(targetDir);
            ctx.CleanDirectory(targetDir);

            var bepInExDir = targetDir.Combine("BepInEx");
            var bepInExCoreDir = bepInExDir.Combine("core");

            var sourceDirectory = ctx.OutputDirectory.Combine(dist.DistributionIdentifier);
            if (dist.FrameworkTarget != null)
                sourceDirectory = sourceDirectory.Combine(dist.FrameworkTarget);

            if (dist.Runtime == "BepisLoader")
            {
                foreach (var filePath in ctx.GetFiles(sourceDirectory.Combine("BepisLoader.*").FullPath))
                {
                    var fileName = filePath.GetFilename().FullPath.ToLower();
                    if (!fileName.EndsWith(".xml"))
                    {
                        ctx.CopyFileToDirectory(filePath, targetDir);
                    }
                }

                // Copy LinuxBootstrap.sh from BepisLoader project directory
                var linuxBootstrapPath = ctx.RootDirectory.CombineWithFilePath("Runtimes/NET/BepisLoader/LinuxBootstrap.sh");
                if (ctx.FileExists(linuxBootstrapPath))
                {
                    ctx.CopyFileToDirectory(linuxBootstrapPath, targetDir);
                    ctx.Log.Information("Copied LinuxBootstrap.sh to distribution");
                }
                else
                {
                    ctx.Log.Warning("LinuxBootstrap.sh not found at: " + linuxBootstrapPath);
                }

                var netCoreCLRSource = ctx.OutputDirectory.Combine("NET.CoreCLR").Combine("net10.0");
                if (ctx.DirectoryExists(netCoreCLRSource))
                {
                    // Create BepInEx directories only if we have files to copy
                    ctx.CreateDirectory(bepInExDir);
                    ctx.CreateDirectory(bepInExCoreDir);

                    foreach (var filePath in ctx.GetFiles(netCoreCLRSource.Combine("*.*").FullPath))
                    {
                        var fileName = filePath.GetFilename().FullPath.ToLower();
                        ctx.CopyFileToDirectory(filePath, bepInExCoreDir);
                    }
                }
                else
                {
                    ctx.Log.Warning($"NET.CoreCLR output directory not found: {netCoreCLRSource}");
                    ctx.Log.Warning("Make sure to build NET.CoreCLR target first if you want BepInEx support");
                }

                // Copy hookfxr files to root directory (excluding readme files and pdb files)
                var hookfxrPath = ctx.CacheDirectory.Combine("hookfxr");
                if (ctx.DirectoryExists(hookfxrPath))
                {
                    foreach (var filePath in ctx.GetFiles(hookfxrPath.Combine("*.*").FullPath))
                    {
                        var fileName = filePath.GetFilename().FullPath.ToLower();
                        if (!fileName.EndsWith(".md") && !fileName.EndsWith(".pdb"))
                        {
                            ctx.CopyFileToDirectory(filePath, targetDir);
                        }
                    }

                    // Update hookfxr.ini to target BepisLoader.dll
                    var hookfxrIniPath = targetDir.CombineWithFilePath("hookfxr.ini");
                    if (ctx.FileExists(hookfxrIniPath))
                    {
                        var iniContent = System.IO.File.ReadAllText(hookfxrIniPath.FullPath);
                        iniContent = iniContent.Replace("enable=true", "enable=false");
                        iniContent = iniContent.Replace("target_assembly=MyApplication.dll", "target_assembly=BepisLoader.dll");
                        iniContent = iniContent.Replace("merge_deps_json=true", "merge_deps_json=false");
                        System.IO.File.WriteAllText(hookfxrIniPath.FullPath, iniContent);
                        ctx.Log.Information("Updated hookfxr.ini to target BepisLoader.dll");
                    }
                }
                else
                {
                    ctx.Log.Warning($"hookfxr cache directory not found: {hookfxrPath}");
                }

                // Replace contents of BepisLoader.runtimeconfig.json with the proper framework configuration for windows
                var runtimeConfigPath = targetDir.CombineWithFilePath("BepisLoader.runtimeconfig.json");
                if (ctx.FileExists(runtimeConfigPath))
                {
                    var runtimeConfig = """
                        {
                          "runtimeOptions": {
                            "tfm": "net10.0",
                            "frameworks": [
                              {
                                "name": "Microsoft.NETCore.App",
                                "version": "10.0.0"
                              },
                              {
                                "name": "Microsoft.WindowsDesktop.App",
                                "version": "10.0.0"
                              }
                            ],
                            "configProperties": {
                              "System.Reflection.Metadata.MetadataUpdater.IsSupported": false,
                              "System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization": false
                            }
                          }
                        }
                        """;
                    System.IO.File.WriteAllText(runtimeConfigPath.FullPath, runtimeConfig);
                    ctx.Log.Information("Updated BepisLoader.runtimeconfig.json with proper framework configuration");
                }
                else
                {
                    ctx.Log.Warning($"BepisLoader.runtimeconfig.json not found at: {runtimeConfigPath}");
                }

                // Copy icon.ico from BepisLoader project directory to BepInEx folder
                var iconPath = ctx.RootDirectory.CombineWithFilePath("Runtimes/NET/BepisLoader/icon.ico");
                if (ctx.FileExists(iconPath))
                {
                    ctx.CopyFileToDirectory(iconPath, bepInExDir);
                    ctx.Log.Information("Copied icon.ico to BepInEx folder");
                }
                else
                {
                    ctx.Log.Warning("icon.ico not found at: " + iconPath);
                }
            }
        }
    }
}

/// <summary>Pushes built packages to the NuGet feed.</summary>
[TaskName("PushNuGet")]
public sealed class PushNuGetTask : FrostingTask<BuildContext>
{
    /// <inheritdoc/>
    public override bool ShouldRun(BuildContext ctx) => !string.IsNullOrWhiteSpace(ctx.NugetApiKey) &&
                                                        ctx.BuildType != BuildContext.ProjectBuildType.Development;

    /// <inheritdoc/>
    public override void Run(BuildContext ctx)
    {
        var nugetPath = ctx.OutputDirectory.Combine("NuGet");
        var settings = new DotNetNuGetPushSettings
        {
            Source = ctx.NugetSource,
            ApiKey = ctx.NugetApiKey
        };
        foreach (var pkg in ctx.GetFiles(nugetPath.Combine("*.nupkg").FullPath))
            ctx.DotNetNuGetPush(pkg, settings);
    }
}

/// <summary>Builds the Thunderstore package for the loader.</summary>
[TaskName("BuildThunderstorePackage")]
[IsDependentOn(typeof(MakeDistTask))]
public sealed class BuildThunderstorePackageTask : FrostingTask<BuildContext>
{
    /// <inheritdoc/>
    public override bool ShouldRun(BuildContext ctx) => ctx.Distributions.Any(d => d.Runtime == "BepisLoader");

    /// <inheritdoc/>
    public override void Run(BuildContext ctx)
    {
        ctx.Log.Information("Building Thunderstore package for BepisLoader...");

        var bepisLoaderProjectPath = ctx.RootDirectory.CombineWithFilePath("Runtimes/NET/BepisLoader/BepisLoader.csproj");

        // Use XmlPeek to read version from csproj (simpler than MSBuild Project)
        var packageVersion = ctx.XmlPeek(bepisLoaderProjectPath, "/Project/PropertyGroup/Version");

        if (string.IsNullOrEmpty(packageVersion))
        {
            throw new Exception("Could not read Version property from BepisLoader.csproj");
        }

        ctx.Log.Information($"Building Thunderstore package with version {packageVersion}...");

        var exitCode = ctx.StartProcess("dotnet", new Cake.Core.IO.ProcessSettings
        {
            Arguments = $"tcli build --package-version {packageVersion}",
            WorkingDirectory = ctx.RootDirectory.Combine("Runtimes/NET/BepisLoader")
        });

        if (exitCode != 0)
        {
            ctx.Log.Error($"dotnet tcli build failed with exit code {exitCode}");
            throw new Exception($"Thunderstore package build failed with exit code {exitCode}");
        }

        ctx.Log.Information("Thunderstore package build completed successfully.");
    }
}

/// <summary>Fixes Linux executable permissions inside the Thunderstore package.</summary>
[TaskName("FixThunderstoreLinuxPermissions")]
[IsDependentOn(typeof(BuildThunderstorePackageTask))]
[SupportedOSPlatform("linux")]
public sealed class FixThunderstoreLinuxPermissionsTask : FrostingTask<BuildContext>
{
    /// <inheritdoc/>
    public override bool ShouldRun(BuildContext ctx) =>
        ctx.Distributions.Any(d => d.Runtime == "BepisLoader") &&
        System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux);

    /// <inheritdoc/>
    [SupportedOSPlatform("linux")]
    public override void Run(BuildContext ctx)
    {
        ctx.Log.Information("Fixing Linux permissions in Thunderstore package...");

        var thunderstorePackageDir = ctx.DistributionDirectory.Combine("thunderstore-package");
        var packageFiles = ctx.GetFiles(thunderstorePackageDir.Combine("*.zip").FullPath);

        foreach (var packageFile in packageFiles)
        {
            ctx.Log.Information($"Fixing Linux permissions in {packageFile.GetFilename()}...");

            var tempDir = ctx.DistributionDirectory.Combine("temp-thunderstore-fix");
            if (ctx.DirectoryExists(tempDir))
            {
                ctx.CleanDirectory(tempDir);
            }
            else
            {
                ctx.CreateDirectory(tempDir);
            }

            System.IO.Compression.ZipFile.ExtractToDirectory(packageFile.FullPath, tempDir.FullPath);

            var executablePermissions = System.IO.UnixFileMode.UserExecute |
                                       System.IO.UnixFileMode.GroupExecute |
                                       System.IO.UnixFileMode.OtherExecute;

            var executableFiles = new[] { "LinuxBootstrap.sh" };

            foreach (var fileName in executableFiles)
            {
                var filePath = tempDir.CombineWithFilePath($"BepInExPack/{fileName}");
                if (ctx.FileExists(filePath))
                {
                    var fileInfo = new System.IO.FileInfo(filePath.FullPath);
                    var originalMode = fileInfo.UnixFileMode;
                    ctx.Log.Information($"Original permissions for {fileName}: {Convert.ToString((int)originalMode, 8).PadLeft(3, '0')} ({originalMode})");

                    fileInfo.UnixFileMode |= executablePermissions;

                    var newMode = fileInfo.UnixFileMode;
                    ctx.Log.Information($"New permissions for {fileName}: {Convert.ToString((int)newMode, 8).PadLeft(3, '0')} ({newMode})");
                }
            }

            ctx.DeleteFile(packageFile);

            System.IO.Compression.ZipFile.CreateFromDirectory(tempDir.FullPath, packageFile.FullPath);

            ctx.Log.Information($"Fixed Linux permissions in {packageFile.GetFilename()}");
        }
    }
}

/// <summary>Zips distributions and writes release metadata.</summary>
[TaskName("Publish")]
[IsDependentOn(typeof(MakeDistTask))]
[IsDependentOn(typeof(PushNuGetTask))]
[IsDependentOn(typeof(FixThunderstoreLinuxPermissionsTask))]
public sealed class PublishTask : FrostingTask<BuildContext>
{
    /// <inheritdoc/>
    public override void Run(BuildContext ctx)
    {
        ctx.Log.Information("Packing BepInEx");

        foreach (var dist in ctx.Distributions)
        {
            var targetZipName = $"BepInEx-{dist.Target}-{ctx.BuildPackageVersion}.zip";
            ctx.Log.Information($"Packing {targetZipName}");
            // https://github.com/cake-build/cake/issues/2592
            System.IO.Compression.ZipFile.CreateFromDirectory(ctx.DistributionDirectory.Combine(dist.Target).FullPath,
                    ctx.DistributionDirectory.CombineWithFilePath(targetZipName).FullPath);
        }


        var changeLog = "";
        if (!string.IsNullOrWhiteSpace(ctx.LastBuildCommit))
        {
            var changeLogContents =
                ctx.Git($"--no-pager log --no-merges --pretty=\"format:<li>(<code>%h</code>) [%an] %s</li>\" {ctx.LastBuildCommit}..HEAD",
                        "\n");
            changeLog = $"<ul>{changeLogContents}</ul>";
        }

        ctx.SerializeJsonToPrettyFile(ctx.DistributionDirectory.CombineWithFilePath("info.json"),
                                      new Dictionary<string, object>
                                      {
                                          ["id"] = ctx.BuildId.ToString(),
                                          ["date"] = DateTime.Now.ToString("o"),
                                          ["changelog"] = changeLog,
                                          ["hash"] = ctx.CurrentCommit.Sha,
                                          ["short_hash"] = ctx.GitShortenSha(ctx.RootDirectory, ctx.CurrentCommit),
                                          ["artifacts"] = ctx.Distributions.Select(d => new Dictionary<string, string>
                                          {
                                              ["file"] = $"BepInEx-{d.Target}-{ctx.BuildPackageVersion}.zip",
                                              ["description"] =
                                                  $"BepInEx {d.Engine} ({d.Runtime}{(d.FrameworkTarget == null ? "" : " " + d.FrameworkTarget)}) for {d.ClearOsName} ({d.Arch}) games"
                                          }).ToArray()
                                      });
    }
}

/// <summary>Default build task.</summary>
[TaskName("Default")]
[IsDependentOn(typeof(CompileTask))]
public class DefaultTask : FrostingTask { }

