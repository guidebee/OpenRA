using System;
using System.IO;
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

                var mapPath = args[0];
                var outputPath = args.Length > 1 ? args[1] : Path.Combine(Path.GetDirectoryName(mapPath), "map.json");

                Console.WriteLine($"Reading map from: {mapPath}");
                
                var mapReader = new MapReader();
                var mapData = mapReader.ReadMap(mapPath);

                // Serialize to JSON with formatting
                var jsonSettings = new JsonSerializerSettings
                {
                    Formatting = Formatting.Indented,
                    NullValueHandling = NullValueHandling.Ignore
                };
                
                var json = JsonConvert.SerializeObject(mapData, jsonSettings);
                
                // Write to output file
                File.WriteAllText(outputPath, json);
                
                Console.WriteLine($"Map data successfully written to: {outputPath}");
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

        static void PrintUsage()
        {
            Console.WriteLine("OpenRA.MapReader - Extracts OpenRA map data to JSON format");
            Console.WriteLine();
            Console.WriteLine("Usage: OpenRA.MapReader <map-path> [output-path]");
            Console.WriteLine();
            Console.WriteLine("  map-path     Path to an OpenRA map folder or .zip file");
            Console.WriteLine("  output-path  Optional path for the output JSON file (default: map.json in the same directory)");
            Console.WriteLine();
            Console.WriteLine("Example: OpenRA.MapReader path/to/map output/map.json");
            Console.WriteLine();
            Console.WriteLine("The JSON output includes information about tileset templates and the Z-order");
            Console.WriteLine("of tiles for overlapping cells, which is useful for rendering the map correctly.");
        }
    }
}
