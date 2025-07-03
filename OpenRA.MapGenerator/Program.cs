using System;
using System.IO;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading.Tasks;
using OpenRA.FileSystem;
using OpenRA.Primitives;

namespace OpenRA.MapGenerator
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            // Set up command-line arguments
            var rootCommand = new RootCommand("OpenRA Map Generator - Creates random maps similar to the JavaScript map generator");

            // Add options
            var sizeOption = new Option<int>(
                "--size",
                () => 96,
                "Size of the map in cells");

            var seedOption = new Option<int?>(
                "--seed",
                "Random seed for map generation (null for random)");

            var tilesetOption = new Option<string>(
                "--tileset",
                () => "temperate",
                "Tileset to use (temperate, desert, snow, etc.)");

            var outputOption = new Option<string>(
                "--output",
                () => "generated-map",
                "Output directory name");

            var playersOption = new Option<int>(
                "--players",
                () => 2,
                "Number of player starting locations");

            var rotationsOption = new Option<int>(
                "--rotations",
                () => 2,
                "Number of rotational symmetry points (1-8)");

            var mirrorOption = new Option<int>(
                "--mirror",
                () => 0,
                "Mirroring mode (0=none, 1=horizontal, 2=vertical, 3=diagonal)");

            var waterOption = new Option<float>(
                "--water",
                () => 0.5f,
                "Water coverage (0.0-1.0)");

            // Add options to command
            rootCommand.AddOption(sizeOption);
            rootCommand.AddOption(seedOption);
            rootCommand.AddOption(tilesetOption);
            rootCommand.AddOption(outputOption);
            rootCommand.AddOption(playersOption);
            rootCommand.AddOption(rotationsOption);
            rootCommand.AddOption(mirrorOption);
            rootCommand.AddOption(waterOption);

            // Set handler
            rootCommand.SetHandler((size, seed, tileset, output, players, rotations, mirror, water) =>
            {
                GenerateMap(size, seed, tileset, output, players, rotations, mirror, water);
            }, sizeOption, seedOption, tilesetOption, outputOption, playersOption, rotationsOption, mirrorOption, waterOption);

            // Execute command
            return await rootCommand.InvokeAsync(args);
        }

        private static void GenerateMap(int size, int? seed, string tileset, string outputFolder, int players, int rotations, int mirror, float water)
        {
            // Use provided seed or generate a random one
            int actualSeed = seed ?? new Random().Next();
            Console.WriteLine($"Generating map with seed: {actualSeed}");

            try
            {
                // Initialize settings
                Game.InitializeSettings(Arguments.Empty);

                // Create the map generator
                Console.WriteLine($"Creating {size}x{size} map with tileset '{tileset}'");
                var mapGenerator = new MapGenerator
                {
                    Size = size,
                    Seed = actualSeed,
                    CustomName = $"Random Map {actualSeed}",
                    Players = players,
                    Rotations = rotations,
                    Mirror = mirror,
                    Water = water
                };

                // Generate the map
                Console.WriteLine("Generating terrain...");
                var map = mapGenerator.Generate();

                // Prepare output directory
                string outputPath = Path.Combine(Environment.CurrentDirectory, outputFolder);
                Console.WriteLine($"Saving map to {outputPath}");

                // Save the map
                map.SaveToFiles(outputPath, mapGenerator);

                Console.WriteLine("Map generated successfully!");
                Console.WriteLine($"Map saved to: {outputPath}");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error generating map: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                Console.ResetColor();
            }
        }
    }
}
