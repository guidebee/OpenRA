using System;
using System.IO;
using System.CommandLine;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenRA.FileSystem;
using OpenRA.Primitives;

namespace OpenRA.TilesetReader
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            // Set up command-line arguments
            var rootCommand = new RootCommand("OpenRA Tileset Reader - Extracts and exports tileset data from game files");

            // Add options
            var outputOption = new Option<string>(
                "--output",
                () => "tileset-export",
                "Output directory name for exported tileset data");

            var gamePathOption = new Option<string>(
                "--game-path",
                "Path to the OpenRA directory or mod directory (containing 'mods' folder, or directly to 'mods/ra' folder)");

            var exportImagesOption = new Option<bool>(
                "--export-images",
                () => true,
                "Whether to export template images along with JSON data");

            // Add options to command
            rootCommand.AddOption(outputOption);
            rootCommand.AddOption(gamePathOption);
            rootCommand.AddOption(exportImagesOption);

            // Set handler
            rootCommand.SetHandler((output, gamePath, exportImages) =>
            {
                ExportAllTilesets(output, gamePath, exportImages);
            }, outputOption, gamePathOption, exportImagesOption);

            // Execute command
            return await rootCommand.InvokeAsync(args);
        }

        private static void ExportAllTilesets(string outputFolder, string gamePath, bool exportImages)
        {
            try
            {
                // Use provided game path or current directory
                string resolvedGamePath = string.IsNullOrEmpty(gamePath)
                    ? Environment.CurrentDirectory
                    : gamePath;

                Console.WriteLine($"Starting with game path: {resolvedGamePath}");

                // Try to find OpenRA directory structure
                resolvedGamePath = ResolveGamePath(resolvedGamePath);

                // Initialize settings for file loading
                Game.InitializeSettings(Arguments.Empty);

                // Create the tileset reader
                var tilesetReader = new TilesetReader(resolvedGamePath);

                // Detect available tilesets
                var tilesets = tilesetReader.GetAvailableTilesets();

                if (tilesets.Count == 0)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"No tilesets found in path: {resolvedGamePath}");
                    Console.WriteLine("Please specify a valid game path with --game-path that points to:");
                    Console.WriteLine("1. The OpenRA root directory (containing the 'mods' folder)");
                    Console.WriteLine("2. The 'mods' directory");
                    Console.WriteLine("3. A specific mod directory (e.g., 'mods/ra')");
                    Console.ResetColor();
                    return;
                }

                // Prepare output directory
                string outputPath = Path.Combine(Environment.CurrentDirectory, outputFolder);
                Directory.CreateDirectory(outputPath);

                // Create the exporter with the same game path
                var exporter = new TilesetExporter(resolvedGamePath);

                // Export each tileset
                Console.WriteLine($"Found {tilesets.Count} tilesets to export: {string.Join(", ", tilesets)}");
                foreach (var tileset in tilesets)
                {
                    try
                    {
                        Console.WriteLine($"Reading tileset '{tileset}'...");
                        var tilesetData = tilesetReader.ReadTileset(tileset);

                        Console.WriteLine($"Tileset '{tileset}' has {tilesetData.Templates.Count} templates");
                        
                        // If no templates were found, skip this tileset
                        if (tilesetData.Templates.Count == 0)
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine($"Warning: No templates found for tileset '{tileset}', skipping export");
                            Console.ResetColor();
                            continue;
                        }

                        // Create a subfolder for this tileset
                        string tilesetOutputPath = Path.Combine(outputPath, tileset.ToLowerInvariant());
                        Console.WriteLine($"Exporting tileset to {tilesetOutputPath}");

                        // Export the data
                        exporter.Export(tilesetData, tilesetOutputPath, exportImages);

                        Console.WriteLine($"Tileset {tileset} exported successfully!");
                    }
                    catch (Exception ex)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"Warning: Error exporting tileset {tileset}: {ex.Message}");
                        Console.WriteLine(ex.StackTrace);
                        Console.ResetColor();
                    }
                }

                Console.WriteLine($"All tilesets exported to: {outputPath}");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error exporting tilesets: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                Console.ResetColor();
            }
        }

        private static string ResolveGamePath(string path)
        {
            Console.WriteLine("Resolving game path...");

            // Check if the path directly contains tilesets and bits directories (mod directory)
            var tilesetsPath = Path.Combine(path, "tilesets");
            var bitsPath = Path.Combine(path, "bits");
            if (Directory.Exists(tilesetsPath) && Directory.Exists(bitsPath))
            {
                Console.WriteLine($"Found mod directory with tilesets and bits folders: {path}");
                return path;
            }

            // Check if this is the OpenRA root directory (contains 'mods' folder)
            var modsPath = Path.Combine(path, "mods");
            if (Directory.Exists(modsPath))
            {
                Console.WriteLine($"Found OpenRA root directory with mods folder: {path}");
                
                // Check for standard mod directories
                var raPath = Path.Combine(modsPath, "ra");
                if (Directory.Exists(raPath))
                {
                    Console.WriteLine($"Found RA mod directory: {raPath}");
                    return raPath;
                }
                
                var cncPath = Path.Combine(modsPath, "cnc");
                if (Directory.Exists(cncPath))
                {
                    Console.WriteLine($"Found CnC mod directory: {cncPath}");
                    return cncPath;
                }
                
                var d2kPath = Path.Combine(modsPath, "d2k");
                if (Directory.Exists(d2kPath))
                {
                    Console.WriteLine($"Found D2K mod directory: {d2kPath}");
                    return d2kPath;
                }
                
                // If no specific mod directory was found, return the mods directory
                Console.WriteLine($"No specific mod directory found, using mods directory: {modsPath}");
                return modsPath;
            }
            
            // Check if we're in the 'mods' directory
            var parentDir = Directory.GetParent(path)?.FullName;
            if (parentDir != null && Path.GetFileName(path).Equals("mods", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"Detected mods directory: {path}");
                
                // Check for RA mod
                var raPath = Path.Combine(path, "ra");
                if (Directory.Exists(raPath))
                {
                    Console.WriteLine($"Found RA mod directory: {raPath}");
                    return raPath;
                }
                
                // Return mods directory as fallback
                return path;
            }
            
            // Check for parent paths that might contain the OpenRA structure
            if (parentDir != null)
            {
                // Check if parent is OpenRA root with mods folder
                var parentModsPath = Path.Combine(parentDir, "mods");
                if (Directory.Exists(parentModsPath))
                {
                    Console.WriteLine($"Found OpenRA root directory in parent path: {parentDir}");
                    
                    // Check for mods/ra/tilesets path
                    var raTilesetsPath = Path.Combine(parentModsPath, "ra", "tilesets");
                    if (Directory.Exists(raTilesetsPath) && Path.GetFileName(path).Equals("tilesets", StringComparison.OrdinalIgnoreCase))
                    {
                        var raPath = Path.Combine(parentModsPath, "ra");
                        Console.WriteLine($"Detected path is tilesets directory, using RA mod directory: {raPath}");
                        return raPath;
                    }
                }
            }
            
            // If none of the above matched, return the original path
            Console.WriteLine($"Using original path: {path}");
            return path;
        }
    }
}
