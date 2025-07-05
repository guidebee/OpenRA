using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using OpenRA.FileSystem;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;

namespace OpenRA.TilesetReader
{
    public class TilesetExporter
    {
        private readonly string gamePath;

        public TilesetExporter(string gamePath)
        {
            this.gamePath = gamePath;
        }

        public void Export(TilesetData tilesetData, string outputPath, bool exportImages)
        {
            try
            {
                // Create output directory if it doesn't exist
                Directory.CreateDirectory(outputPath);

                // Export tileset information to a single consolidated JSON file
                ExportTilesetDataJson(tilesetData, outputPath);

                // Export templates to individual JSON files (for easier inspection)
                var templatesDir = Path.Combine(outputPath, "templates");
                Directory.CreateDirectory(templatesDir);
                foreach (var template in tilesetData.Templates.Values)
                {
                    ExportTemplate(template, templatesDir);
                }

                // Export images if requested
                if (exportImages)
                {
                    var imagesDir = Path.Combine(outputPath, "images");
                    Directory.CreateDirectory(imagesDir);

                    ExportTemplateImages(tilesetData, imagesDir);
                }

                Console.WriteLine($"Exported {tilesetData.Templates.Count} templates for tileset {tilesetData.Name}");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error during export: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                Console.ResetColor();
            }
        }

        private void ExportTilesetDataJson(TilesetData tilesetData, string outputPath)
        {
            // Create a consolidated JSON structure that matches the MapReader format
            var tilesetJson = new
            {
                name = tilesetData.Name,
                templates = tilesetData.Templates.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new
                    {
                        id = kvp.Value.Id,
                        name = $"Template{kvp.Value.Id}",
                        size = kvp.Value.Size,
                        tiles = kvp.Value.Tiles.Select(t => new
                        {
                            index = t.Index,
                            terrainType = t.TerrainType,
                            height = t.Height,
                            rampType = t.RampType
                        }).ToList()
                    }
                ),
                terrainTypes = tilesetData.TerrainTypes.Select(t => new
                {
                    index = t.Index,
                    name = t.Name,
                    isPassable = t.IsPassable
                }).ToList()
            };

            var jsonFilePath = Path.Combine(outputPath, $"{tilesetData.Name.ToLowerInvariant()}.json");
            var json = JsonConvert.SerializeObject(tilesetJson, Formatting.Indented);
            File.WriteAllText(jsonFilePath, json);

            Console.WriteLine($"Exported tileset data to {jsonFilePath}");
        }

        private void ExportTemplate(TemplateExportInfo template, string outputDir)
        {
            var templateFilePath = Path.Combine(outputDir, $"template_{template.Id}.json");

            // Serialize the template to JSON
            var json = JsonConvert.SerializeObject(template, Formatting.Indented);

            // Write the JSON to file
            File.WriteAllText(templateFilePath, json);
        }

        private void ExportTemplateImages(TilesetData tilesetData, string imagesDir)
        {
            // Create a file system to read the original images
            var modDataLoader = new ModDataLoader();
            // Use the same game path that the TilesetReader used
            var modData = modDataLoader.CreateFolderMods(new[] { gamePath });
            var fileSystem = modData.ModFiles;

            // Get all available files
            var allFiles = ((TilesetFileSystem)fileSystem).GetAllFileNames().ToList();

            // Process each template
            foreach (var template in tilesetData.Templates.Values)
            {
                try
                {
                    // Get possible image names for this template
                    var imageNames = template.GetTilesetImages(tilesetData.Name);
                    
                    // Track if we found any images
                    bool foundImage = false;
                    
                    foreach (var imageName in imageNames)
                    {
                        // Find the image file (case-insensitive search)
                        var matchingFiles = allFiles
                            .Where(f => Path.GetFileName(f).Equals(imageName, StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        if (matchingFiles.Any())
                        {
                            foundImage = true;
                            var sourceFilePath = matchingFiles.First();
                            var destFilePath = Path.Combine(imagesDir, Path.GetFileName(sourceFilePath)); // Preserve original case

                            try
                            {
                                // Process and export the image
                                ExportImage(fileSystem, sourceFilePath, destFilePath, template);
                                Console.WriteLine($"Exported image: {Path.GetFileName(sourceFilePath)}");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Warning: Failed to export image {imageName}: {ex.Message}");
                            }
                        }
                    }

                    // If no images were found, generate a placeholder
                    if (!foundImage)
                    {
                        var defaultImageName = $"t{template.Id:D2}{GetTilesetExtension(tilesetData.Name)}";
                        Console.WriteLine($"Warning: No image found for template {template.Id}. Generating placeholder.");
                        GeneratePlaceholderImage(template, defaultImageName, imagesDir);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Error processing template {template.Id}: {ex.Message}");
                }
            }
        }

        private void ExportImage(IReadOnlyFileSystem fileSystem, string sourceFilePath, string destFilePath, TemplateExportInfo template)
        {
            using (var sourceStream = fileSystem.Open(sourceFilePath))
            {
                // Debug the source file size
                Console.WriteLine($"Processing image: {sourceFilePath}, Size: {sourceStream.Length} bytes");

                // First try to load as a standard image format
                try
                {
                    using var image = Image.Load<Rgba32>(sourceStream);

                    // Process the image - calculate color ranges for visualization
                    CalculateColorRanges(image, template);

                    // Save the processed image
                    image.Save(destFilePath);
                    return;
                }
                catch (Exception ex)
                {
                    // If image loading fails, reset stream and continue with direct copy
                    Console.WriteLine($"Failed to load as standard image: {ex.Message}. Copying as raw data.");
                    sourceStream.Position = 0;
                }

                // Copy the file directly if it can't be loaded as an image
                using (var destStream = File.Create(destFilePath))
                {
                    sourceStream.CopyTo(destStream);
                }
            }
        }

        private void CalculateColorRanges(Image<Rgba32> image, TemplateExportInfo template)
        {
            // Calculate tile dimensions in the image
            int tileWidth = Math.Max(1, image.Width / template.Size.X);
            int tileHeight = Math.Max(1, image.Height / template.Size.Y);

            foreach (var tile in template.Tiles)
            {
                // Calculate tile position in the image
                int tileX = (tile.Index % template.Size.X) * tileWidth;
                int tileY = (tile.Index / template.Size.X) * tileHeight;

                // Initialize min/max color values
                int[] minColor = new[] { 255, 255, 255, 255 };
                int[] maxColor = new[] { 0, 0, 0, 255 };

                // Scan the tile area to find color ranges
                for (int y = 0; y < tileHeight; y++)
                {
                    for (int x = 0; x < tileWidth; x++)
                    {
                        if (tileX + x < image.Width && tileY + y < image.Height)
                        {
                            Rgba32 pixel = image[tileX + x, tileY + y];

                            // Update min values (RGB only)
                            minColor[0] = Math.Min(minColor[0], pixel.R);
                            minColor[1] = Math.Min(minColor[1], pixel.G);
                            minColor[2] = Math.Min(minColor[2], pixel.B);

                            // Update max values (RGB only)
                            maxColor[0] = Math.Max(maxColor[0], pixel.R);
                            maxColor[1] = Math.Max(maxColor[1], pixel.G);
                            maxColor[2] = Math.Max(maxColor[2], pixel.B);
                        }
                    }
                }

                // Update the tile color ranges
                tile.MinColor = minColor;
                tile.MaxColor = maxColor;
            }
        }

        private void GeneratePlaceholderImage(TemplateExportInfo template, string imageName, string imagesDir)
        {
            var destFilePath = Path.Combine(imagesDir, imageName);

            try
            {
                // Create a placeholder image based on the template size
                const int tileSize = 24; // Default tile size in pixels
                int width = template.Size.X * tileSize;
                int height = template.Size.Y * tileSize;

                using (var image = new Image<Rgba32>(width, height))
                {
                    // Fill with a default background color
                    image.Mutate(x => x.Fill(Color.FromRgb(180, 180, 180)));

                    // Add a grid pattern and color tiles based on terrain type and height
                    for (int y = 0; y < template.Size.Y; y++)
                    {
                        for (int x = 0; x < template.Size.X; x++)
                        {
                            // Calculate tile index
                            int index = y * template.Size.X + x;
                            
                            // Draw tile border
                            var tileRect = new Rectangle(x * tileSize, y * tileSize, tileSize, tileSize);
                            image.Mutate(ctx => ctx.Draw(Color.Black, 1, tileRect));

                            // Color the tile if it exists in the template
                            var tile = template.Tiles.FirstOrDefault(t => t.Index == index);
                            if (tile != null)
                            {
                                // Color based on terrain type and height
                                byte r = (byte)Math.Min(255, 120 + (tile.TerrainType * 20) + (tile.Height * 5));
                                byte g = (byte)Math.Min(255, 120 + (tile.TerrainType * 10) - (tile.Height * 8));
                                byte b = (byte)Math.Min(255, 170 - (tile.TerrainType * 10));
                                
                                var tileColor = Color.FromRgb(r, g, b);

                                // Fill the tile
                                image.Mutate(ctx => ctx.Fill(
                                    tileColor,
                                    new Rectangle(
                                        x * tileSize + 1,
                                        y * tileSize + 1,
                                        tileSize - 2,
                                        tileSize - 2)));

                                // Add a height indicator in the corner
                                if (tile.Height > 0)
                                {
                                    // Draw a small rectangle for height indication
                                    var heightRect = new Rectangle(
                                        x * tileSize + 2,
                                        y * tileSize + 2,
                                        6, 
                                        6);
                                    
                                    var heightColor = tile.Height switch {
                                        <= 3 => Color.FromRgb(200, 200, 100),
                                        <= 7 => Color.FromRgb(230, 150, 80),
                                        _ => Color.FromRgb(240, 100, 70)
                                    };
                                    
                                    image.Mutate(ctx => ctx.Fill(heightColor, heightRect));
                                }
                            }
                        }
                    }

                    // Save the placeholder image
                    image.Save(destFilePath);
                    Console.WriteLine($"Generated placeholder image: {imageName}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to generate placeholder image {imageName}: {ex.Message}");
            }
        }

        private string GetTilesetExtension(string tileset)
        {
            return tileset.ToUpperInvariant() switch
            {
                "TEMPERAT" => ".tem",
                "SNOW" => ".sno",
                "DESERT" => ".des",
                "INTERIOR" => ".int",
                "JUNGLE" => ".jun",
                _ => ".tem" // Default to temperate
            };
        }
    }
}
