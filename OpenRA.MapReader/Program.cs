using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace OpenRA.MapReader
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                if (args.Length < 1 || args[0] == "--help" || args[0] == "-h")
                {
                    PrintUsage();
                    return;
                }

                var inputPath = args[0];
                var outputPath = args.Length > 1 ? args[1] : Path.Combine(Directory.GetCurrentDirectory(), "maps_parsed");

                if (IsDirectory(inputPath))
                {
                    // Process all maps in directory
                    ProcessMapsDirectory(inputPath, outputPath);
                }
                else
                {
                    // Process single map
                    ProcessSingleMap(inputPath, outputPath);
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error: {ex.Message}");
                Console.ResetColor();
                
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner error: {ex.InnerException.Message}");
                }
                
                Console.WriteLine("Use --help for usage information");
            }
        }

        static void ProcessMapsDirectory(string inputDirectory, string outputDirectory)
        {
            Console.WriteLine($"Scanning directory: {inputDirectory}");
            
            // Count maps for progress reporting
            var mapCount = 0;
            var processedCount = 0;

            // Find all potential map directories and zip files
            var mapDirectories = Directory.GetDirectories(inputDirectory, "*", SearchOption.AllDirectories);
            var mapZipFiles = Directory.GetFiles(inputDirectory, "*.zip", SearchOption.AllDirectories);
            
            // Filter to only directories that contain map.yaml and map.bin
            var validMapDirectories = new List<string>();
            
            foreach (var dir in mapDirectories)
            {
                if (File.Exists(Path.Combine(dir, "map.yaml")) && File.Exists(Path.Combine(dir, "map.bin")))
                    validMapDirectories.Add(dir);
            }
            
            mapCount = validMapDirectories.Count + mapZipFiles.Length;
            Console.WriteLine($"Found {mapCount} maps to process");
            
            // Create map reader instance
            var mapReader = new MapReader();
            var jsonSettings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore
            };
            
            // Process map directories
            foreach (var mapDir in validMapDirectories)
            {
                try
                {
                    processedCount++;
                    Console.WriteLine($"[{processedCount}/{mapCount}] Processing map directory: {mapDir}");
                    
                    // Determine relative path to maintain directory structure
                    var relativePath = Path.GetRelativePath(inputDirectory, mapDir);
                    var mapOutputDir = Path.Combine(outputDirectory, relativePath);
                    
                    // Create output directory if it doesn't exist
                    Directory.CreateDirectory(mapOutputDir);
                    
                    // Process the map
                    var mapData = mapReader.ReadMap(mapDir);
                    var json = JsonConvert.SerializeObject(mapData, jsonSettings);
                    
                    // Write to output file
                    var jsonPath = Path.Combine(mapOutputDir, "map.json");
                    File.WriteAllText(jsonPath, json);
                    
                    Console.WriteLine($"Map data written to: {jsonPath}");
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"Warning: Failed to process map {mapDir}: {ex.Message}");
                    Console.ResetColor();
                }
            }
            
            // Process zip files
            foreach (var zipFile in mapZipFiles)
            {
                try
                {
                    processedCount++;
                    Console.WriteLine($"[{processedCount}/{mapCount}] Processing map archive: {zipFile}");
                    
                    // Determine relative path to maintain directory structure
                    var relativePath = Path.GetRelativePath(inputDirectory, Path.GetDirectoryName(zipFile));
                    var zipFileName = Path.GetFileNameWithoutExtension(zipFile);
                    var mapOutputDir = Path.Combine(outputDirectory, relativePath, zipFileName);
                    
                    // Create output directory if it doesn't exist
                    Directory.CreateDirectory(mapOutputDir);
                    
                    // Process the map
                    var mapData = mapReader.ReadMap(zipFile);
                    var json = JsonConvert.SerializeObject(mapData, jsonSettings);
                    
                    // Write to output file
                    var jsonPath = Path.Combine(mapOutputDir, "map.json");
                    File.WriteAllText(jsonPath, json);
                    
                    Console.WriteLine($"Map data written to: {jsonPath}");
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"Warning: Failed to process map {zipFile}: {ex.Message}");
                    Console.ResetColor();
                }
            }
            
            Console.WriteLine($"Map processing complete. Successfully processed {processedCount} maps.");
            Console.WriteLine($"Output directory: {outputDirectory}");
        }

        static void ProcessSingleMap(string mapPath, string outputPath)
        {
            Console.WriteLine($"Reading map from: {mapPath}");
            
            var mapReader = new MapReader();
            var mapData = mapReader.ReadMap(mapPath);

            // Determine output path
            string jsonPath;
            if (Directory.Exists(outputPath))
            {
                // If output is a directory, use map.json as the filename
                var mapName = Path.GetFileNameWithoutExtension(mapPath);
                if (IsDirectory(mapPath))
                    mapName = Path.GetFileName(mapPath);
                
                jsonPath = Path.Combine(outputPath, mapName + ".json");
            }
            else
            {
                // Use the provided path as the exact output file
                jsonPath = outputPath;
            }
            
            // Create directory if it doesn't exist
            Directory.CreateDirectory(Path.GetDirectoryName(jsonPath));
            
            // Serialize to JSON with formatting
            var jsonSettings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore
            };
            
            var json = JsonConvert.SerializeObject(mapData, jsonSettings);
            
            // Write to output file
            File.WriteAllText(jsonPath, json);
            
            Console.WriteLine($"Map data successfully written to: {jsonPath}");
        }

        static bool IsDirectory(string path)
        {
            try
            {
                return Directory.Exists(path);
            }
            catch
            {
                return false;
            }
        }

        static void PrintUsage()
        {
            Console.WriteLine("OpenRA.MapReader - Extracts OpenRA map data to JSON format");
            Console.WriteLine();
            Console.WriteLine("Usage: OpenRA.MapReader <input-path> [output-path]");
            Console.WriteLine();
            Console.WriteLine("  input-path   Path to an OpenRA map folder, .zip file, or directory containing multiple maps");
            Console.WriteLine("  output-path  Optional path for the output JSON file or directory (default: ./maps_parsed)");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  OpenRA.MapReader path/to/map output/map.json       # Process a single map");
            Console.WriteLine("  OpenRA.MapReader path/to/map.zip output/parsed/    # Process a single zipped map");
            Console.WriteLine("  OpenRA.MapReader path/to/maps/ output/maps_parsed/ # Process all maps in directory");
            Console.WriteLine();
            Console.WriteLine("When processing a directory, the original directory structure will be preserved in the output.");
            Console.WriteLine("The JSON output includes information about tileset templates and the Z-order");
            Console.WriteLine("of tiles for overlapping cells, which is useful for rendering the map correctly.");
        }
    }
}
