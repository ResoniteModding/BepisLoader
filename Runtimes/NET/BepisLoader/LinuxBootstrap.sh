#!/usr/bin/env sh

# This is a bootstrapper to run Resonite in what is quite the unorthodox
# configuration on Linux.
#
# This script is executed by the "Renderite.Boot" bootstrapper if it
# detects it's running under Proton/Wine.
#
# The following shell code is responsible for installing an adequate runtime
# for Resonite that's independent from any system installation (or lack thereof)
# and for ensuring that Renderite is executed with Proton's Wine installation
# and not the system's.


### SCRIPT START ###

# Ensure that $DOTNET_ROOT points to the downloaded runtime rather
# than at the system's. Also ensure that the root and tools subfolder
# are temporarily added to the system's $PATH variable just to make
# sure things work correctly.

DOTNET_ROOT="$PWD/dotnet-runtime"
PATH="$PATH":"$DOTNET_ROOT":"$DOTNET_ROOT"/tools


# Miscellaneous variables

DOTNET_INSTALL_SCRIPT="$PWD/dotnet-install.sh"
DOTNET_EXECUTABLE="$DOTNET_ROOT/dotnet"

# terminal_execute()
# {
# 	# !! Modified by Cyro !! (Change: terminal emulator order)
# 	#
# 	# This code is released in public domain by Han Boetes <han@mijncomputer.nl>
# 	#
# 	# This script tries to exec a terminal emulator by trying some known terminal
# 	# emulators.
# 	#
# 	# We welcome patches that add distribution-specific mechanisms to find the
# 	# preferred terminal emulator. On Debian, there is the x-terminal-emulator
# 	# symlink for example.
# 	#
# 	# Invariants:
# 	# 1. $TERMINAL comes first.
# 	# 2. The most common terminal emulators from popular desktop environments are tried afterwards.
# 	# 4. More niche/less well-known terminal emulators are tried next.
# 	# 3. Distribution-specific mechanisms come last (since they're kind of clunky), e.g. x-terminal-emulator, uxterm, xterm, etc.

# 	for terminal in "$TERMINAL" konsole gnome-terminal mate-terminal xfce4-terminal guake terminix tilix lxterminal terminator termite terminology tilda hyper alacritty termit kitty Eterm rio roxterm st lilyterm wezterm qterminal x-terminal-emulator uxterm xterm aterm urxvt rxvt; do
# 		if command -v "$terminal" > /dev/null 2>&1; then
# 			"$terminal" -e "$* 2>/dev/null"
# 			break
# 		fi
# 	done

# 	# "$*" 2>/dev/null
# }

# Overload the 'dotnet' command so that it calls the version which was
# downloaded by the script rather than potentially call (or fail to call)
# the system's main dotnet runtime.

dotnet()
{
	"$DOTNET_EXECUTABLE" "$@"
}

main()
{
	# Make sure the dotnet installer is executable, grab the .NET 9.0 runtime and
	# just place it in the main Resonite folder.

	chmod +x "$DOTNET_INSTALL_SCRIPT"

	# Install .NET 10 into the current directory

	bash "$DOTNET_INSTALL_SCRIPT" --verbose --channel 10.0 --runtime dotnet --install-dir "$DOTNET_ROOT"


	# Not technically required, but mark dotnet itself as executable just in case.

	chmod +x "$DOTNET_EXECUTABLE"


	# Replace Windows runtime config with Linux runtime config for BepisLoader
	if [ -f "./BepisLoader.runtimeconfig.json" ]; then
		cat > "./BepisLoader.runtimeconfig.json" <<-'EOF'
		{
		  "runtimeOptions": {
		    "tfm": "net10.0",
		    "framework": {
		      "name": "Microsoft.NETCore.App",
		      "version": "10.0.0"
		    },
		    "configProperties": {
		      "System.Reflection.Metadata.MetadataUpdater.IsSupported": false,
		      "System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization": false
		    }
		  }
		}
		EOF
	fi

	echo "Parsing hookfxr parameters"
	HOOKFXR_STATUS=""
	TARGET_ASSEMBLY=""

	# CLI arg takes priority
	for arg in "$@" ; do
		if [ "$arg" = "--hookfxr-enable" ] ; then
			HOOKFXR_STATUS="ENABLED"
			echo "hookfxr is enabled by CLI"
		fi
		if [ "$arg" = "--hookfxr-disable" ] ; then
			HOOKFXR_STATUS="DISABLED"
			echo "hookfxr is disabled by CLI"
		fi
		TARGET_ARG=${arg#"--hookfxr-target="}
		if [ ! "$arg" = "$TARGET_ARG" ] ; then
			TARGET_ASSEMBLY="$TARGET_ARG"
			echo "hookfxr target forced to $TARGET_ASSEMBLY by CLI"
		fi
	done

	# If not specified in CLI args, check INI
	if [ -z $HOOKFXR_STATUS ] ; then
		if grep -q "enable=true" hookfxr.ini ; then
			HOOKFXR_STATUS="ENABLED"
			echo "hookfxr is enabled by INI"
		fi
		if grep -q "enable=false" hookfxr.ini ; then
			HOOKFXR_STATUS="DISABLED"
			echo "hookfxr is disabled by INI"
		fi		
	fi
	
	ENTRY_POINT="Renderite.Host.dll"

	# If hookfxr is enabled, change the entry point
	if [ "$HOOKFXR_STATUS" = "ENABLED" ] ; then
		# Only read from INI if it was not already found in CLI
		if [ -z $TARGET_ASSEMBLY ]; then
			TARGET_ASSEMBLY=$(sed -n "s/^\s*target_assembly=\(.*\S\)\s*$/\1/p" hookfxr.ini)
		fi

		if [ -n $TARGET_ASSEMBLY ]; then
			ENTRY_POINT="$TARGET_ASSEMBLY"
		fi
	fi
	echo "Entry point: $ENTRY_POINT"

	# ~ Launch Resonite! :) ~

	dotnet "$ENTRY_POINT" "$@"
}

main "$@"
exit
