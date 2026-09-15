<p align="center">
    <img src="https://raw.githubusercontent.com/ResoniteModding/BepisLoader/master/Runtimes/NET/BepisLoader/icon.png">
</p>

# BepisLoader
[![Thunderstore Version](https://img.shields.io/badge/dynamic/json?url=https%3A%2F%2Fthunderstore.io%2Fapi%2Fexperimental%2Fpackage%2FResoniteModding%2FBepisLoader%2F&query=%24.latest.version_number&label=Thunderstore&style=flat&logo=thunderstore&logoColor=white)](https://thunderstore.io/c/resonite/p/ResoniteModding/BepisLoader/)
[![Thunderstore Downloads](https://img.shields.io/badge/dynamic/json?url=https%3A%2F%2Fthunderstore.io%2Fapi%2Fv1%2Fpackage-metrics%2FResoniteModding%2FBepisLoader%2F&query=%24.downloads&label=downloads&style=flat&logo=thunderstore&logoColor=white)](https://thunderstore.io/c/resonite/p/ResoniteModding/BepisLoader/)
[![NuGet Version](https://img.shields.io/badge/dynamic/json?url=https%3A%2F%2Fnuget-modding.resonite.net%2Fv3%2Fregistration%2Fbepinex.net.coreclr%2Findex.json&query=%24.items%5B0%5D.upper&label=NuGet&style=flat&logo=nuget&logoColor=white&color=004880)](https://nuget-modding.resonite.net/packages/bepinex.net.coreclr)
[![Build](https://img.shields.io/github/actions/workflow/status/ResoniteModding/BepisLoader/build.yml?style=flat&logo=github)](https://github.com/ResoniteModding/BepisLoader/actions/workflows/build.yml)
[![Docs](https://img.shields.io/badge/docs-modding.resonite.net-blue?style=flat&logo=gitbook&logoColor=white)](https://modding.resonite.net/)
[![License](https://img.shields.io/badge/license-MIT_%2B_LGPL--2.1--only-blue?style=flat)](#license)
[![Discord](https://img.shields.io/discord/901126079857692714?style=flat&logo=discord&logoColor=white&label=Discord)](https://discord.gg/vCDJK9xyvm)

A mod loader which allows using BepInEx with [Resonite](https://resonite.com/).

BepisLoader is a .NET (Core) only mod loader, currently targeting .NET 10. It is built on a fork of [BepInEx](https://github.com/BepInEx/BepInEx) with all Unity aspects (Unity Mono, IL2CPP, UnityDoorstop, etc.) removed. It cannot load Unity plugins and does not support Unity games.

## Installation

For the recommended mod-manager setup, see the [official installation guide](https://modding.resonite.net/getting-started/installation/).

### Manual

1. Download the latest release ZIP file (e.g., `ResoniteModding-BepisLoader-1.6.0.zip`) from [Thunderstore](https://thunderstore.io/c/resonite/p/ResoniteModding/BepisLoader/).
2. Extract the contents of the `BepInExPack` folder from the ZIP into your Resonite installation directory:
   - **Windows Default:** `C:\Program Files (x86)\Steam\steamapps\common\Resonite\`
   - **Linux Default:** `~/.steam/steam/steamapps/common/Resonite/`
3. **Linux Users Only:** The included `LinuxBootstrap.sh` file needs to be used instead of the default one:
   - The package includes a modified `LinuxBootstrap.sh` that launches `BepisLoader.dll` instead of `Renderite.Host.dll`
   - **Important:** Resonite updates could replace this file, breaking the mod loader. If this happens, you'll need to manually replace `LinuxBootstrap.sh` with the one from the BepisLoader package.
     - Some mod managers - including Gale - will copy the script from their managed profile folder to the Resonite install directory on every game launch. In which case, you do not need to replace it manually.
4. Enable the modded entry point by replacing `enable=false` with `enable=true` in the file `hookfxr.ini`
   - Alternatively, add `--hookfxr-enable` to your launch arguments. 
5. Start the game normally.
6. If you want to verify that the mod loader is working, check the `BepInEx\LogOutput.log` file after launching the game.

### Disabling temporarily
1. Set `enable=false` in the file `hookfxr.ini`
   - Alternatively, add `--hookfxr-disable` to your launch arguments.
2. If you had added `--hookfxr-enable` to your launch arguments before, you must remove it. 

### Uninstallation

1. Delete the following files from your Resonite installation directory:
   - **Windows Entry Points:**
     - `hostfxr.dll` (our Windows entry point)
     - `hostfxr.pdb`
   - **Common Files:**
     - `hookfxr.ini`
     - All `BepisLoader*` files
2. Delete the `BepInEx` folder.

## Package Contents

The BepisLoader package contains:
- **Windows Entry Point:** `hostfxr.dll` and `hookfxr.ini` for hooking into the .NET runtime
- **Linux Entry Point:** Modified `LinuxBootstrap.sh` that launches BepisLoader if hookfxr is enabled (in `hookfxr.ini` or with launch options)
- **BepisLoader:** Core loader files (`BepisLoader.dll`, etc.)
- **BepInEx:** The BepInEx framework and all required dependencies

## Building

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

On Windows:
```powershell
./build.ps1 --target Publish -v d --build-type BleedingEdge
```

On Linux:
```bash
./build.sh --target Publish -v d --build-type BleedingEdge
```

Or call the build project directly with `dotnet` (works on any OS):

```bash
dotnet run --project build/Build.csproj -- --target Publish -v d --build-type BleedingEdge
```

Build output (dist zips and the Thunderstore package) lands in `bin/dist`.

### Targets (`--target`)

| Target | What it does |
|---|---|
| `Default` | Compiles the solution (runs `Clean` → `RestoreTools` → `Compile`). This is what runs if `--target` is omitted. |
| `Clean` | Wipes the `bin` output directory. |
| `RestoreTools` | Restores `dotnet` tools. |
| `Compile` | Builds the solution. |
| `DownloadDependencies` | Downloads the external hookfxr dependency into the dep cache. |
| `MakeDist` | Assembles the distributable folders under `bin/dist` (BepInEx pack layout, Thunderstore `BepInExPack`). |
| `BuildThunderstorePackage` | Runs `tcli build` to produce the Thunderstore `.zip` under `bin/dist/thunderstore-package`. |
| `FixThunderstoreLinuxPermissions` | Fixes executable permissions inside the Thunderstore package (Linux/macOS). |
| `PushNuGet` | Pushes `*.nupkg` to `--nuget-source`. Skipped unless `--nuget-api-key` is set and `--build-type` is not `Development`. |
| `Publish` | Full pipeline: `MakeDist` + `PushNuGet` + `FixThunderstoreLinuxPermissions`, then packs the final `BepInEx-*.zip` files. |

### Arguments

| Argument | Default | What it does |
|---|---|---|
| `--build-type` | `Development` | `Release`, `Development`, or `BleedingEdge`. Controls the version suffix (`""`, `-dev+<sha>`, `-be.<build-id>+<sha>`). |
| `--build-id` | `-1` | Build number, used in the `BleedingEdge` version suffix (`be.<build-id>`). |
| `--last-build-commit` | `""` | If set, `Publish` generates a changelog in `info.json` from commits since this ref. |
| `--nuget-api-key` | `""` | API key for `PushNuGet`. Pushing is skipped when unset. |
| `--nuget-source` | `https://nuget.bepinex.dev/v3/index.json` | NuGet feed used by `PushNuGet`. |

Standard Cake Frosting options also work: `--target`, `--verbosity`/`-v` (`Quiet`, `Minimal`, `Normal`, `Verbose`, `Diagnostic` — e.g. `-v d`), `--exclusive`, `--dryrun`, `--showtree`, `--showdescription`, `--help`.

## References

BepisLoader makes use of these repositories and packages them inside its releases:

- [BepInEx](https://github.com/ResoniteModding/BepInEx) - The core BepInEx framework
- [BepInEx Resonite Shim](https://github.com/ResoniteModding/BepInExResoniteShim) - Resonite-specific compatibility layer
- [hookfxr](https://github.com/ResoniteModding/hookfxr) - .NET runtime hooking for Windows

## License

- BepisLoader entry point (Runtimes\NET\BepisLoader) is licensed under the [MIT License](./LICENSE).
- The bundled BepInEx framework is licensed under the LGPL-2.1 (see `LICENSE` at the root of the repository).
