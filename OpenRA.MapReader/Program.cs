using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using OpenRA.FileSystem;
using OpenRA.Primitives;

namespace OpenRA.MapReader
{
    /// <summary>
    /// Template information structure for JSON export
    /// </summary>
    public class TemplateExportInfo
    {
        public ushort Id { get; set; }
        public int2 Size { get; set; }
        public bool PickAny { get; set; }
        public string[] Categories { get; set; }
        public string[] Images { get; set; }
        public string[] DepthImages { get; set; }
        public int[] Frames { get; set; }
        public string Palette { get; set; }
        public List<TemplateTileExportInfo> Tiles { get; set; } = new List<TemplateTileExportInfo>();
    }

    /// <summary>
    /// Tile information structure for JSON export
    /// </summary>
    public class TemplateTileExportInfo
    {
        public int Index { get; set; }
        public byte TerrainType { get; set; }
        public byte Height { get; set; }
        public byte RampType { get; set; }
        public int[] MinColor { get; set; }
        public int[] MaxColor { get; set; }
    }

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

                // Create templates directory
                var templatesDir = Path.Combine(outputPath, "templates");
                Directory.CreateDirectory(templatesDir);

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
                
                // After processing maps, extract template information from them
                ExtractTemplateInformation(outputPath, templatesDir);
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

        static void ExtractTemplateInformation(string mapsOutputDir, string templatesDir)
        {
            Console.WriteLine("Extracting template information from processed maps...");
            
            var jsonSettings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore
            };
            
            var jsonFiles = Directory.GetFiles(mapsOutputDir, "map.json", SearchOption.AllDirectories);
            
            if (jsonFiles.Length == 0)
            {
                Console.WriteLine("No map JSON files found to extract template information from.");
                return;
            }
            
            var tilesetTemplates = new Dictionary<string, Dictionary<ushort, TemplateExportInfo>>();
            
            // Extract template information from each map
            foreach (var jsonFile in jsonFiles)
            {
                try
                {
                    var json = File.ReadAllText(jsonFile);
                    var mapData = JsonConvert.DeserializeObject<MapJsonFormat>(json);
                    
                    if (mapData == null || string.IsNullOrEmpty(mapData.Tileset))
                        continue;
                    
                    var tileset = mapData.Tileset;
                    
                    // Create dictionary for this tileset if it doesn't exist
                    if (!tilesetTemplates.ContainsKey(tileset))
                        tilesetTemplates[tileset] = new Dictionary<ushort, TemplateExportInfo>();
                    
                    // Add templates from this map
                    foreach (var template in mapData.Templates)
                    {
                        // Skip if we already have this template
                        if (tilesetTemplates[tileset].ContainsKey(template.Key))
                            continue;
                        
                        var templateInfo = new TemplateExportInfo
                        {
                            Id = template.Value.Id,
                            Size = template.Value.Size,
                            Tiles = template.Value.Tiles.Select(t => new TemplateTileExportInfo
                            {
                                Index = t.Index,
                                TerrainType = t.TerrainType,
                                Height = t.Height,
                                MinColor = GetColorForTerrainType(t.TerrainType),
                                MaxColor = GetColorForTerrainType(t.TerrainType)
                            }).ToList()
                        };
                        
                        tilesetTemplates[tileset][template.Key] = templateInfo;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Failed to extract template info from {jsonFile}: {ex.Message}");
                }
            }
            
            // Export template information for each tileset
            foreach (var kvp in tilesetTemplates)
            {
                var tileset = kvp.Key;
                var templates = kvp.Value;
                
                Console.WriteLine($"Processing tileset '{tileset}' with {templates.Count} templates...");
                
                // Create directory for this tileset
                var tilesetDir = Path.Combine(templatesDir, tileset);
                Directory.CreateDirectory(tilesetDir);
                
                // Create a template index file for this tileset
                var templateIndex = new Dictionary<string, object>
                {
                    ["tileset"] = tileset,
                    ["templateCount"] = templates.Count,
                    ["templates"] = templates.Keys.OrderBy(k => k).Select(k => new { id = k, name = $"Template{k}" }).ToList()
                };
                
                // Save the index file
                var indexJson = JsonConvert.SerializeObject(templateIndex, jsonSettings);
                File.WriteAllText(Path.Combine(tilesetDir, "index.json"), indexJson);
                
                // Save individual template files
                foreach (var template in templates)
                {
                    var templateId = template.Key;
                    var templateData = template.Value;
                    
                    // Create directory for this template
                    var templateDir = Path.Combine(tilesetDir, templateId.ToString());
                    Directory.CreateDirectory(templateDir);
                    
                    // Save template JSON
                    var templateJson = JsonConvert.SerializeObject(templateData, jsonSettings);
                    File.WriteAllText(Path.Combine(templateDir, $"{templateId}.json"), templateJson);
                    
                    // Generate a placeholder image for visualization
                    GenerateTemplatePlaceholderImage(templateData, Path.Combine(templateDir, $"{templateId}.png"));
                }
                
                Console.WriteLine($"Exported {templates.Count} templates for tileset '{tileset}'");
            }
            
            Console.WriteLine($"Template extraction complete. Templates saved to {templatesDir}");
        }
        
        static int[] GetColorForTerrainType(byte terrainType)
        {
            // Generate a color based on the terrain type
            switch (terrainType)
            {
                case 0: // Clear terrain
                    return new[] { 76, 230, 0, 255 };
                case 1: // Water
                    return new[] { 0, 160, 255, 255 };
                case 2: // Rough
                    return new[] { 170, 120, 70, 255 };
                case 3: // Rock
                    return new[] { 120, 120, 120, 255 };
                case 4: // Beach
                    return new[] { 255, 215, 0, 255 };
                case 5: // Ore
                    return new[] { 224, 180, 0, 255 };
                default:
                    // Generate a color based on the terrain type value
                    var hue = (terrainType * 37) % 360;
                    var r = 0;
                    var g = 0;
                    var b = 0;
                    
                    if (hue < 60)
                    {
                        r = 255;
                        g = (int)(255 * hue / 60.0);
                    }
                    else if (hue < 120)
                    {
                        r = (int)(255 * (120 - hue) / 60.0);
                        g = 255;
                    }
                    else if (hue < 180)
                    {
                        g = 255;
                        b = (int)(255 * (hue - 120) / 60.0);
                    }
                    else if (hue < 240)
                    {
                        g = (int)(255 * (240 - hue) / 60.0);
                        b = 255;
                    }
                    else if (hue < 300)
                    {
                        b = 255;
                        r = (int)(255 * (hue - 240) / 60.0);
                    }
                    else
                    {
                        b = (int)(255 * (360 - hue) / 60.0);
                        r = 255;
                    }
                    
                    return new[] { r, g, b, 255 };
            }
        }
        
        static void GenerateTemplatePlaceholderImage(TemplateExportInfo template, string outputPath)
        {
            try
            {
                int width = template.Size.X;
                int height = template.Size.Y;
                
                if (width <= 0 || height <= 0)
                {
                    width = 1;
                    height = 1;
                }
                
                int scale = 32;
                int imageWidth = width * scale;
                int imageHeight = height * scale;
                
                using (var writer = new StreamWriter(outputPath + ".ppm"))
                {
                    writer.WriteLine("P3");
                    writer.WriteLine($"{imageWidth} {imageHeight}");
                    writer.WriteLine("255");
                    
                    for (int y = 0; y < imageHeight; y++)
                    {
                        for (int x = 0; x < imageWidth; x++)
                        {
                            int tileX = x / scale;
                            int tileY = y / scale;
                            int tileIndex = tileY * width + tileX;
                            
                            var tileInfo = template.Tiles.FirstOrDefault(t => t.Index == tileIndex);
                            
                            int[] color = tileInfo?.MinColor ?? new[] { 128, 128, 128, 255 };
                            
                            if (x % scale == 0 || y % scale == 0 || x % scale == scale - 1 || y % scale == scale - 1)
                            {
                                color = new[] { 0, 0, 0, 255 };
                            }
                            
                            writer.Write($"{color[0]} {color[1]} {color[2]} ");
                        }
                        writer.WriteLine();
                    }
                }
                
                ConvertPpmToPng(outputPath + ".ppm", outputPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to generate placeholder image for template {template.Id}: {ex.Message}");
            }
        }
        
        static void ConvertPpmToPng(string ppmPath, string pngPath)
        {
            try
            {
                string[] lines = File.ReadAllLines(ppmPath);
                
                if (lines.Length < 4 || lines[0] != "P3")
                    throw new InvalidDataException("Invalid PPM format");
                
                string[] dimensions = lines[1].Split(' ');
                int width = int.Parse(dimensions[0]);
                int height = int.Parse(dimensions[1]);
                
                using (var writer = new StreamWriter(pngPath + ".html"))
                {
                    writer.WriteLine("<!DOCTYPE html>");
                    writer.WriteLine("<html>");
                    writer.WriteLine("<head><title>Template Visualization</title></head>");
                    writer.WriteLine("<body style='background-color: #f0f0f0;'>");
                    writer.WriteLine($"<h2>Template {Path.GetFileNameWithoutExtension(pngPath)}</h2>");
                    writer.WriteLine($"<svg width='{width}' height='{height}' viewBox='0 0 {width} {height}' xmlns='http://www.w3.org/2000/svg'>");
                    
                    string pixelData = string.Join(" ", lines.Skip(3));
                    string[] pixels = pixelData.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    
                    int pixelIndex = 0;
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            if (pixelIndex + 2 < pixels.Length)
                            {
                                int r = int.Parse(pixels[pixelIndex++]);
                                int g = int.Parse(pixels[pixelIndex++]);
                                int b = int.Parse(pixels[pixelIndex++]);
                                
                                writer.WriteLine($"<rect x='{x}' y='{y}' width='1' height='1' fill='rgb({r},{g},{b})' />");
                            }
                        }
                    }
                    
                    writer.WriteLine("</svg>");
                    writer.WriteLine("</body>");
                    writer.WriteLine("</html>");
                }
                
                File.Delete(ppmPath);
                
                Console.WriteLine($"Created HTML visualization: {pngPath}.html");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to convert PPM to HTML: {ex.Message}");
            }
        }

        static void ProcessMapsDirectory(string inputDirectory, string outputDirectory)
        {
            Console.WriteLine($"Scanning directory: {inputDirectory}");
            
            var mapCount = 0;
            var processedCount = 0;

            var mapDirectories = Directory.GetDirectories(inputDirectory, "*", SearchOption.AllDirectories);
            var mapZipFiles = Directory.GetFiles(inputDirectory, "*.zip", SearchOption.AllDirectories);
            
            var validMapDirectories = new List<string>();
            
            foreach (var dir in mapDirectories)
            {
                if (File.Exists(Path.Combine(dir, "map.yaml")) && File.Exists(Path.Combine(dir, "map.bin")))
                    validMapDirectories.Add(dir);
            }
            
            mapCount = validMapDirectories.Count + mapZipFiles.Length;
            Console.WriteLine($"Found {mapCount} maps to process");
            
            var mapReader = new MapReader();
            var jsonSettings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore
            };
            
            foreach (var mapDir in validMapDirectories)
            {
                try
                {
                    processedCount++;
                    Console.WriteLine($"[{processedCount}/{mapCount}] Processing map directory: {mapDir}");
                    
                    var relativePath = Path.GetRelativePath(inputDirectory, mapDir);
                    var mapOutputDir = Path.Combine(outputDirectory, relativePath);
                    
                    Directory.CreateDirectory(mapOutputDir);
                    
                    var mapData = mapReader.ReadMap(mapDir);
                    var json = JsonConvert.SerializeObject(mapData, jsonSettings);
                    
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
            
            foreach (var zipFile in mapZipFiles)
            {
                try
                {
                    processedCount++;
                    Console.WriteLine($"[{processedCount}/{mapCount}] Processing map archive: {zipFile}");
                    
                    var relativePath = Path.GetRelativePath(inputDirectory, Path.GetDirectoryName(zipFile));
                    var zipFileName = Path.GetFileNameWithoutExtension(zipFile);
                    var mapOutputDir = Path.Combine(outputDirectory, relativePath, zipFileName);
                    
                    Directory.CreateDirectory(mapOutputDir);
                    
                    var mapData = mapReader.ReadMap(zipFile);
                    var json = JsonConvert.SerializeObject(mapData, jsonSettings);
                    
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

            string jsonPath;
            if (Directory.Exists(outputPath))
            {
                var mapName = Path.GetFileNameWithoutExtension(mapPath);
                if (IsDirectory(mapPath))
                    mapName = Path.GetFileName(mapPath);
                
                jsonPath = Path.Combine(outputPath, mapName + ".json");
            }
            else
            {
                jsonPath = outputPath;
            }
            
            Directory.CreateDirectory(Path.GetDirectoryName(jsonPath));
            
            var jsonSettings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore
            };
            
            var json = JsonConvert.SerializeObject(mapData, jsonSettings);
            
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
            Console.WriteLine();
            Console.WriteLine("In addition to map data, tileset templates will be exported to a 'templates' subdirectory");
            Console.WriteLine("organized by tileset with individual JSON files for each template and visualization images.");
        }
    }
}
