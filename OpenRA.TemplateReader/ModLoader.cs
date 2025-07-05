using System;
using System.IO;
using System.Collections.Generic;
using OpenRA.Primitives;

namespace OpenRA.TemplateReader
{
	public class ModLoader
	{
		public ModData LoadModData(string modId, string gamePath)
		{
			// Initialize the log channels to prevent the debug channel error
			try
			{
				Log.AddChannel("debug", "debug.log");
				Log.AddChannel("perf", "perf.log");
				Log.AddChannel("sync", "sync.log");
				Log.AddChannel("server", "server.log");
				Log.AddChannel("sound", "sound.log");
				Log.AddChannel("graphics", "graphics.log");
				Log.AddChannel("geoip", "geoip.log");
				Log.AddChannel("filesystem", "filesystem.log");
			}
			catch (ArgumentException)
			{
				// Channel already exists, ignore
			}

			// Set the engine directory to the current directory for file resolution
			var engineDir = Path.GetFullPath(gamePath);
			Environment.SetEnvironmentVariable("ENGINE_DIR", engineDir);

			// Create mods directory if it doesn't exist
			var modsPath = Path.Combine(gamePath, "mods");
			if (!Directory.Exists(modsPath))
				Directory.CreateDirectory(modsPath);

			// Create mods/common directory if it doesn't exist
			var commonModPath = Path.Combine(modsPath, "common");
			if (!Directory.Exists(commonModPath))
				Directory.CreateDirectory(commonModPath);
				
			// Create a minimal mod.yaml in common to make it a valid mod
			var commonModYamlPath = Path.Combine(commonModPath, "mod.yaml");
			if (!File.Exists(commonModYamlPath))
			{
				const string MinimalCommonModYaml = @"Metadata:
	Title: Common
	Version: {DEV_VERSION}
	Hidden: true

PackageFormats: []

FileSystem:
	Type: Folder
	RootPath: .

Packages:
	.";
				File.WriteAllText(commonModYamlPath, MinimalCommonModYaml);
			}

			// Initialize game settings
			Game.InitializeSettings(Arguments.Empty);
			
			// Configure mod search paths
			var modSearchPaths = new[] { Path.Combine(gamePath, "mods") };
			var mods = new InstalledMods(modSearchPaths, new[] { modId });
			if (!mods.ContainsKey(modId))
				throw new Exception($"Mod '{modId}' not found in path '{modSearchPaths[0]}'");

			var manifest = mods[modId];
			
			// Create a CustomFileSystemLoader field in the manifest to override the default
			// This is a reflection-based hack since we can't modify the Manifest.FileSystem directly
			var fieldInfo = typeof(Manifest).GetField("yaml", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
			if (fieldInfo != null)
			{
				var yamlDict = (Dictionary<string, MiniYaml>)fieldInfo.GetValue(manifest);
				yamlDict["FileSystem"] = new MiniYaml("Folder", new List<MiniYamlNode> { new MiniYamlNode("RootPath", ".") });
			}
			
			// Create a mod data instance with useLoadScreen=false to avoid trying to use a UI
			var modData = new ModData(manifest, mods, false);
			
			// Make sure any paths like ^EngineDir are resolved
			modData.ModFiles.Mount("^", engineDir);
			modData.ModFiles.Mount("^EngineDir", engineDir);

			// Ensure mod directories exist
			var modDirectory = Path.Combine(engineDir, "mods", modId);
			EnsureDirectoryExists(modDirectory);
			
			// Create a minimal mod.yaml in the mod directory if it doesn't exist
			var modYamlPath = Path.Combine(modDirectory, "mod.yaml");
			if (!File.Exists(modYamlPath))
			{
				var minimalModYaml = @"Metadata:
	Title: " + modId + @"
	Version: {DEV_VERSION}

RequiresMods:
	common: {DEV_VERSION}

FileSystem:
	Type: Folder
	RootPath: .

Packages:
	.
	./mods/common: common

MapFolders:
	.";
				File.WriteAllText(modYamlPath, minimalModYaml);
			}
			
			// Create a minimal mod package structure
			EnsureDirectoryExists(Path.Combine(modDirectory, "maps"));
			EnsureDirectoryExists(Path.Combine(modDirectory, "sequences"));
			EnsureDirectoryExists(Path.Combine(modDirectory, "tilesets"));
			
			// Create an empty map folder structure - important to prevent errors
			var mapsDir = Path.Combine(modDirectory, "maps");
			File.WriteAllText(Path.Combine(mapsDir, "map.bin"), string.Empty);
			
			// Ensure common mod directory exists since it's often required
			var commonModDir = Path.Combine(engineDir, "mods", "common");
			EnsureDirectoryExists(commonModDir);

			return modData;
		}

		static void EnsureDirectoryExists(string path)
		{
			if (!Directory.Exists(path))
				Directory.CreateDirectory(path);
		}
	}
}
