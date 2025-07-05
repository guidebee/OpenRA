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
                "Path to the game files (optional, uses current directory if not specified)");

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

                // Try to find mods/ra/tilesets if it exists
                var modsTilesetPath = Path.Combine(resolvedGamePath, "..", "mods", "ra", "tilesets");
                if (Directory.Exists(modsTilesetPath))
                {
                    Console.WriteLine($"Found RA tilesets folder: {modsTilesetPath}");
                    resolvedGamePath = modsTilesetPath;
                }

                Console.WriteLine($"Initializing OpenRA engine...");

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
                    Console.WriteLine("Please specify a valid game path with --game-path");
                    Console.ResetColor();
                    return;
                }

                // Prepare output directory
                string outputPath = Path.Combine(Environment.CurrentDirectory, outputFolder);
                Directory.CreateDirectory(outputPath);

                // Create the exporter
                var exporter = new TilesetExporter();

                // Export each tileset
                Console.WriteLine($"Found {tilesets.Count} tilesets to export");
                foreach (var tileset in tilesets)
                {
                    try
                    {
                        Console.WriteLine($"Reading tileset '{tileset}'...");
                        var tilesetData = tilesetReader.ReadTileset(tileset);

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
    }
}
