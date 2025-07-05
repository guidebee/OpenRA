using System;
using System.CommandLine;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using OpenRA.FileSystem;
using OpenRA.Primitives;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.Fonts;

namespace OpenRA.TemplateReader
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            // Set up command-line arguments
            var rootCommand = new RootCommand("OpenRA Template Reader - Extracts and exports single template data from game files");

            // Add options
            var outputOption = new Option<string>(
                "--output",
                () => "template-export",
                "Output directory name for exported template data");

            var gamePathOption = new Option<string>(
                "--game-path",
                "Path to the OpenRA directory or mod directory (containing 'mods' folder, or directly to 'mods/ra' folder)");

            var tilesetOption = new Option<string>(
                "--tileset",
                "The tileset name (e.g., 'desert', 'temperat', 'snow')");

            var templateIdOption = new Option<ushort>(
                "--template-id",
                "The template ID to export (e.g., 401)");

            // Add options to command
            rootCommand.AddOption(outputOption);
            rootCommand.AddOption(gamePathOption);
            rootCommand.AddOption(tilesetOption);
            rootCommand.AddOption(templateIdOption);

            // Make tileset and templateId required
            tilesetOption.IsRequired = true;
            templateIdOption.IsRequired = true;

            // Set handler
            rootCommand.SetHandler((output, gamePath, tileset, templateId) =>
            {
                ExportTemplate(output, gamePath, tileset, templateId);
            }, outputOption, gamePathOption, tilesetOption, templateIdOption);

            // Execute command
            return await rootCommand.InvokeAsync(args);
        }

        private static void ExportTemplate(string outputFolder, string gamePath, string tileset, ushort templateId)
        {
            try
            {
                Console.WriteLine($"Exporting template {templateId} from tileset '{tileset}'");

                // Use provided game path or current directory
                string resolvedGamePath = string.IsNullOrEmpty(gamePath)
                    ? Environment.CurrentDirectory
                    : gamePath;

                Console.WriteLine($"Starting with game path: {resolvedGamePath}");

                // Try to find OpenRA directory structure
                resolvedGamePath = ResolveGamePath(resolvedGamePath);

                // Initialize settings for file loading
                Game.InitializeSettings(Arguments.Empty);

                // Create the template reader
                var templateReader = new TemplateReader(resolvedGamePath);

                // Read the template
                var templateData = templateReader.ReadTemplate(tileset, templateId);

                if (templateData == null)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Template {templateId} not found in tileset '{tileset}'");
                    Console.ResetColor();
                    return;
                }

                // Prepare output directory
                string outputPath = Path.Combine(Environment.CurrentDirectory, outputFolder);
                Directory.CreateDirectory(outputPath);

                // Export the template data as JSON
                var jsonSettings = new JsonSerializerSettings
                {
                    Formatting = Formatting.Indented,
                    NullValueHandling = NullValueHandling.Ignore
                };

                var templateJson = JsonConvert.SerializeObject(templateData, jsonSettings);
                var jsonFilePath = Path.Combine(outputPath, $"{tileset}_template_{templateId}.json");
                File.WriteAllText(jsonFilePath, templateJson);
                Console.WriteLine($"Template data exported to: {jsonFilePath}");

                // Export the template image
                var imageFilePath = Path.Combine(outputPath, $"{tileset}_template_{templateId}.png");
                GenerateTemplatePng(templateData, imageFilePath, tileset);
                Console.WriteLine($"Template image exported to: {imageFilePath}");

                // If the template has images specified, also try to export the original images
                if (templateData.Images != null && templateData.Images.Length > 0)
                {
                    var imageExporter = new TemplateImageExporter(resolvedGamePath);
                    foreach (var imageName in templateData.Images)
                    {
                        if (imageExporter.ExportImage(imageName, outputPath))
                        {
                            Console.WriteLine($"Exported original image: {imageName}");
                        }
                        else
                        {
                            Console.WriteLine($"Failed to export original image: {imageName}");
                        }
                    }
                }

                Console.WriteLine($"Template {templateId} from tileset '{tileset}' exported successfully!");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error exporting template: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                Console.ResetColor();
            }
        }

        private static void GenerateTemplatePng(TemplateExportInfo template, string outputPath, string tileset)
        {
            try
            {
                // Calculate dimensions
                const int scale = 24; // Size of each tile in pixels
                int imageWidth = template.Size.X * scale;
                int imageHeight = template.Size.Y * scale;

                // Create a new image
                using (var image = new Image<Rgba32>(imageWidth, imageHeight))
                {
                    // Fill with background color
                    image.Mutate(ctx => ctx.Fill(SixLabors.ImageSharp.Color.LightGray));

                    // Draw each tile
                    for (int y = 0; y < template.Size.Y; y++)
                    {
                        for (int x = 0; x < template.Size.X; x++)
                        {
                            // Calculate tile index
                            int tileIndex = y * template.Size.X + x;

                            // Find corresponding tile info
                            var tileInfo = template.Tiles.FirstOrDefault(t => t.Index == tileIndex);

                            if (tileInfo != null)
                            {
                                int tileX = x * scale;
                                int tileY = y * scale;

                                DrawTile(image, tileX, tileY, scale, tileInfo, tileset);
                            }
                        }
                    }

                    // Add a border around the template for clarity
                    image.Mutate(ctx => 
                    {
                        var borderColor = SixLabors.ImageSharp.Color.Black;
                        ctx.Draw(borderColor, 2, new SixLabors.ImageSharp.Rectangle(0, 0, imageWidth, imageHeight));
                        
                        // Draw template ID and info in the bottom-right corner
                        try
                        {
                            var font = SystemFonts.CreateFont("Arial", 16, FontStyle.Bold);
                            var templateInfo = $"Template {template.Id} - {tileset}";
                            if (template.Images != null && template.Images.Length > 0)
                            {
                                templateInfo += $" ({string.Join(", ", template.Images)})";
                            }
                            
                            // Draw text with background for better visibility
                            var textColor = SixLabors.ImageSharp.Color.White;
                            var bgColor = new SixLabors.ImageSharp.Color(new SixLabors.ImageSharp.PixelFormats.Rgba32(0, 0, 0, 180));
                            var textSize = TextMeasurer.Measure(templateInfo, new TextOptions(font));
                            var textPos = new PointF(imageWidth - textSize.Width - 10, imageHeight - textSize.Height - 10);
                            
                            ctx.Fill(bgColor, new RectangleF(textPos.X - 5, textPos.Y - 5, textSize.Width + 10, textSize.Height + 10));
                            ctx.DrawText(templateInfo, font, textColor, textPos);
                        }
                        catch (Exception textEx)
                        {
                            Console.WriteLine($"Warning: Could not render text: {textEx.Message}");
                        }
                    });

                    // Save the image
                    image.Save(outputPath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to generate template PNG: {ex.Message}");
                
                // Create a minimal fallback image
                try
                {
                    using (var fallbackImage = new Image<Rgba32>(200, 100))
                    {
                        fallbackImage.Mutate(ctx => 
                        {
                            ctx.Fill(SixLabors.ImageSharp.Color.LightGray);
                            ctx.Draw(SixLabors.ImageSharp.Color.Black, 2, new SixLabors.ImageSharp.Rectangle(0, 0, 200, 100));
                            
                            try
                            {
                                var font = SystemFonts.CreateFont("Arial", 12);
                                ctx.DrawText($"Template {template.Id}\nTileset: {tileset}\nSize: {template.Size.X}x{template.Size.Y}", 
                                    font, SixLabors.ImageSharp.Color.Black, new PointF(10, 10));
                            }
                            catch
                            {
                                // Ignore font errors in fallback
                            }
                        });
                        
                        fallbackImage.Save(outputPath);
                    }
                }
                catch
                {
                    Console.WriteLine("Could not create fallback image");
                }
            }
        }

        private static void DrawTile(Image<Rgba32> image, int x, int y, int size, TemplateTileExportInfo tileInfo, string tileset)
        {
            // Get base color by terrain type and height
            byte terrainType = tileInfo.TerrainType;
            byte height = tileInfo.Height;
            byte rampType = tileInfo.RampType;
            
            // Create a color based on the terrain type and height
            SixLabors.ImageSharp.Color tileColor;
            
            switch (tileset.ToUpperInvariant())
            {
                case "DESERT":
                    tileColor = DrawDesertTerrain(terrainType, height, rampType);
                    break;
                case "TEMPERAT":
                    tileColor = DrawTemperatTerrain(terrainType, height, rampType);
                    break;
                case "SNOW":
                    tileColor = DrawSnowTerrain(terrainType, height, rampType);
                    break;
                case "INTERIOR":
                    tileColor = DrawInteriorTerrain(terrainType, height, rampType);
                    break;
                default:
                    tileColor = DrawGenericTerrain(terrainType, height, rampType);
                    break;
            }
            
            // Draw the tile
            image.Mutate(ctx =>
            {
                // Fill the tile with the base color
                ctx.Fill(tileColor, new SixLabors.ImageSharp.Rectangle(x, y, size, size));
                
                // Draw a border around the tile
                ctx.Draw(new SixLabors.ImageSharp.Color(new SixLabors.ImageSharp.PixelFormats.Rgba32(0, 0, 0, 100)), 1, new SixLabors.ImageSharp.Rectangle(x, y, size, size));
                
                // Draw height value in the center of the tile
                try
                {
                    var font = SystemFonts.CreateFont("Arial", 10);
                    var heightText = height.ToString();
                    var terrainText = $"T{terrainType}";
                    
                    ctx.DrawText(heightText, font, SixLabors.ImageSharp.Color.Black, new PointF(x + size / 2 - 5, y + size / 2 - 5));
                    ctx.DrawText(terrainText, font, SixLabors.ImageSharp.Color.Black, new PointF(x + 2, y + 2));
                }
                catch
                {
                    // Ignore text rendering errors
                }
                
                // If it's a ramp, indicate that
                if (rampType > 0)
                {
                    // Draw diagonal line to indicate ramp
                    ctx.DrawLines(SixLabors.ImageSharp.Color.Red, 2, new PointF(x, y + size), new PointF(x + size, y));
                }
            });
        }

        private static SixLabors.ImageSharp.Color DrawDesertTerrain(byte terrainType, byte height, byte rampType)
        {
            // Desert palette - yellows, browns, tans
            byte r, g, b;
            
            switch (terrainType)
            {
                case 0: // Sand
                    r = (byte)(230 - height * 5);
                    g = (byte)(210 - height * 5);
                    b = (byte)(160 - height * 5);
                    break;
                case 1: // Dunes
                    r = (byte)(220 - height * 4);
                    g = (byte)(190 - height * 4);
                    b = (byte)(140 - height * 4);
                    break;
                case 2: // Rock
                    r = (byte)(180 - height * 4);
                    g = (byte)(150 - height * 4);
                    b = (byte)(110 - height * 4);
                    break;
                case 3: // Cliff
                    r = (byte)(150 - height * 3);
                    g = (byte)(120 - height * 3);
                    b = (byte)(90 - height * 3);
                    break;
                default: // Other terrain types
                    r = (byte)(200 - height * 5);
                    g = (byte)(180 - height * 5);
                    b = (byte)(130 - height * 5);
                    break;
            }
            
            return new SixLabors.ImageSharp.Color(new SixLabors.ImageSharp.PixelFormats.Rgba32(r, g, b, 255));
        }

        private static SixLabors.ImageSharp.Color DrawTemperatTerrain(byte terrainType, byte height, byte rampType)
        {
            // Temperate palette - greens, browns
            byte r, g, b;
            
            switch (terrainType)
            {
                case 0: // Clear
                    r = (byte)(160 - height * 4);
                    g = (byte)(200 - height * 4);
                    b = (byte)(120 - height * 4);
                    break;
                case 1: // Rough
                    r = (byte)(140 - height * 3);
                    g = (byte)(170 - height * 3);
                    b = (byte)(90 - height * 3);
                    break;
                case 2: // Rock
                    r = (byte)(150 - height * 4);
                    g = (byte)(140 - height * 4);
                    b = (byte)(120 - height * 4);
                    break;
                case 3: // Road
                    r = (byte)(170 - height * 3);
                    g = (byte)(160 - height * 3);
                    b = (byte)(140 - height * 3);
                    break;
                case 4: // Water
                    r = (byte)(100 - height * 2);
                    g = (byte)(130 - height * 2);
                    b = (byte)(190 - height * 2);
                    break;
                default: // Other terrain types
                    r = (byte)(150 - height * 4);
                    g = (byte)(180 - height * 4);
                    b = (byte)(110 - height * 4);
                    break;
            }
            
            return new SixLabors.ImageSharp.Color(new SixLabors.ImageSharp.PixelFormats.Rgba32(r, g, b, 255));
        }

        private static SixLabors.ImageSharp.Color DrawSnowTerrain(byte terrainType, byte height, byte rampType)
        {
            // Snow palette - whites, light blues
            byte r, g, b;
            
            switch (terrainType)
            {
                case 0: // Snow
                    r = (byte)(240 - height * 3);
                    g = (byte)(240 - height * 3);
                    b = (byte)(250 - height * 3);
                    break;
                case 1: // Ice
                    r = (byte)(210 - height * 4);
                    g = (byte)(230 - height * 4);
                    b = (byte)(255 - height * 4);
                    break;
                case 2: // Rock
                    r = (byte)(160 - height * 4);
                    g = (byte)(160 - height * 4);
                    b = (byte)(180 - height * 4);
                    break;
                case 3: // Road
                    r = (byte)(150 - height * 3);
                    g = (byte)(150 - height * 3);
                    b = (byte)(160 - height * 3);
                    break;
                default: // Other terrain types
                    r = (byte)(210 - height * 5);
                    g = (byte)(220 - height * 5);
                    b = (byte)(240 - height * 5);
                    break;
            }
            
            return new SixLabors.ImageSharp.Color(new SixLabors.ImageSharp.PixelFormats.Rgba32(r, g, b, 255));
        }

        private static SixLabors.ImageSharp.Color DrawInteriorTerrain(byte terrainType, byte height, byte rampType)
        {
            // Interior palette - grays, tans
            byte r, g, b;
            
            switch (terrainType)
            {
                case 0: // Floor
                    r = (byte)(180 - height * 3);
                    g = (byte)(180 - height * 3);
                    b = (byte)(180 - height * 3);
                    break;
                case 1: // Wall
                    r = (byte)(140 - height * 3);
                    g = (byte)(140 - height * 3);
                    b = (byte)(140 - height * 3);
                    break;
                default: // Other terrain types
                    r = (byte)(160 - height * 4);
                    g = (byte)(160 - height * 4);
                    b = (byte)(160 - height * 4);
                    break;
            }
            
            return new SixLabors.ImageSharp.Color(new SixLabors.ImageSharp.PixelFormats.Rgba32(r, g, b, 255));
        }

        private static SixLabors.ImageSharp.Color DrawGenericTerrain(byte terrainType, byte height, byte rampType)
        {
            // Generic visualization for unknown tilesets
            byte baseValue = (byte)(200 - (terrainType * 20));
            byte r = (byte)(baseValue - height * 5);
            byte g = (byte)(baseValue - height * 3);
            byte b = (byte)(baseValue - height * 8);
            
            return new SixLabors.ImageSharp.Color(new SixLabors.ImageSharp.PixelFormats.Rgba32(r, g, b, 255));
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
            
            // If none of the above matched, return the original path
            Console.WriteLine($"Using original path: {path}");
            return path;
        }
    }
}
