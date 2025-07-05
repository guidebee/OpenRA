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
        public void Export(TilesetData tilesetData, string outputPath, bool exportImages)
        {
            // Create output directory if it doesn't exist
            Directory.CreateDirectory(outputPath);

            // Create templates directory
            var templatesDir = Path.Combine(outputPath, "templates");
            Directory.CreateDirectory(templatesDir);

            // Export tileset index
            ExportTilesetIndex(tilesetData, outputPath);

            // Export each template
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

        private void ExportTilesetIndex(TilesetData tilesetData, string outputPath)
        {
            var indexFilePath = Path.Combine(outputPath, $"{tilesetData.Name.ToLowerInvariant()}_index.json");

            // Serialize the index to JSON
            var json = JsonConvert.SerializeObject(tilesetData.Index, Formatting.Indented);

            // Write the JSON to file
            File.WriteAllText(indexFilePath, json);

            Console.WriteLine($"Exported tileset index to {indexFilePath}");
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
            var modData = modDataLoader.CreateFolderMods(new[] { Environment.CurrentDirectory });
            var fileSystem = modData.ModFiles;

            foreach (var template in tilesetData.Templates.Values)
            {
                foreach (var imageName in template.GetTilesetImages(tilesetData.Name))
                {
                    // Find the image file
                    var matchingFiles = ((TilesetFileSystem)fileSystem).GetAllFileNames()
                        .Where(f => Path.GetFileName(f).Equals(imageName, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (matchingFiles.Any())
                    {
                        var sourceFilePath = matchingFiles.First();
                        var destFilePath = Path.Combine(imagesDir, imageName);

                        try
                        {
                            // Process the image
                            ProcessTemplateImage(fileSystem, sourceFilePath, destFilePath, template);

                            Console.WriteLine($"Exported image: {imageName}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Warning: Failed to export image {imageName}: {ex.Message}");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Warning: Image not found: {imageName}");

                        // Try to generate a placeholder image
                        TryGeneratePlaceholderImage(template, imageName, imagesDir);
                    }
                }
            }
        }

        private void ProcessTemplateImage(IReadOnlyFileSystem fileSystem, string sourceFilePath, string destFilePath, TemplateExportInfo template)
        {
            try
            {
                // For OpenRA template files, they may be in a custom format
                // If we can't process them directly, just copy the file
                using (var sourceStream = fileSystem.Open(sourceFilePath))
                {
                    // First attempt to load as a standard image format
                    try
                    {
                        using var image = Image.Load<Rgba32>(sourceStream);

                        // Process image - resize, add borders, etc. as needed
                        image.Mutate(x => x.Brightness(1.1f));  // Example: slightly brighten

                        // Calculate color ranges for each tile in the template
                        CalculateColorRanges(image, template);

                        // Save the processed image
                        image.Save(destFilePath);
                        return;
                    }
                    catch
                    {
                        // If standard image loading fails, reset stream and continue with direct copy
                        sourceStream.Position = 0;
                    }

                    // Direct copy as fallback
                    using (var destStream = File.Create(destFilePath))
                    {
                        sourceStream.CopyTo(destStream);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Error processing image: {ex.Message}");

                // Fallback to direct copy
                using (var sourceStream = fileSystem.Open(sourceFilePath))
                using (var destStream = File.Create(destFilePath))
                {
                    sourceStream.CopyTo(destStream);
                }
            }
        }

        private void CalculateColorRanges(Image<Rgba32> image, TemplateExportInfo template)
        {
            int tileWidth = image.Width / template.Size.X;
            int tileHeight = image.Height / template.Size.Y;

            foreach (var tile in template.Tiles)
            {
                // Calculate tile position in the image
                int tileX = (tile.Index % template.Size.X) * tileWidth;
                int tileY = (tile.Index / template.Size.X) * tileHeight;

                // Initialize min/max color values
                int[] minColor = new[] { 255, 255, 255 };
                int[] maxColor = new[] { 0, 0, 0 };

                // Scan the tile area to find color ranges
                for (int y = 0; y < tileHeight; y++)
                {
                    for (int x = 0; x < tileWidth; x++)
                    {
                        if (tileX + x < image.Width && tileY + y < image.Height)
                        {
                            Rgba32 pixel = image[tileX + x, tileY + y];

                            // Update min values
                            minColor[0] = Math.Min(minColor[0], pixel.R);
                            minColor[1] = Math.Min(minColor[1], pixel.G);
                            minColor[2] = Math.Min(minColor[2], pixel.B);

                            // Update max values
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

        private void TryGeneratePlaceholderImage(TemplateExportInfo template, string imageName, string imagesDir)
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
                    // Fill with a default color
                    image.Mutate(x => x.Fill(Color.FromRgb(200, 200, 200)));

                    // Add a grid pattern
                    for (int y = 0; y < template.Size.Y; y++)
                    {
                        for (int x = 0; x < template.Size.X; x++)
                        {
                            // Draw a border around each tile
                            var tileRect = new Rectangle(x * tileSize, y * tileSize, tileSize, tileSize);
                            image.Mutate(ctx => ctx.Draw(Color.Black, 1, tileRect));

                            // Add tile index text
                            int index = y * template.Size.X + x;
                            if (template.Tiles.Any(t => t.Index == index))
                            {
                                var tile = template.Tiles.First(t => t.Index == index);

                                // Color the tile based on height
                                var tileColor = Color.FromRgb(
                                    (byte)(155 + tile.Height * 5),
                                    (byte)(155 - tile.Height * 5),
                                    (byte)155);

                                image.Mutate(ctx => ctx.Fill(
                                    tileColor,
                                    new Rectangle(
                                        x * tileSize + 1,
                                        y * tileSize + 1,
                                        tileSize - 2,
                                        tileSize - 2)));
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
    }
}
