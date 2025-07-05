using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenRA.FileSystem;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace OpenRA.TemplateReader
{
    public class TemplateImageExporter
    {
        private readonly string gamePath;
        private readonly IReadOnlyFileSystem fileSystem;
        private readonly Dictionary<string, string> imagePathCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly MixLoader mixLoader;
        private readonly TemplateConverter templateConverter;

        public TemplateImageExporter(string gamePath)
        {
            this.gamePath = gamePath;

            // Create file system to access mod files
            var modDataLoader = new ModDataLoader();
            var modData = modDataLoader.CreateFolderMods(new[] { gamePath });
            fileSystem = modData.ModFiles;
            
            // Initialize MIX file loader
            mixLoader = new MixLoader();
            templateConverter = new TemplateConverter();
        }

        public bool ExportImage(string imageName, string outputDir)
        {
            try
            {
                // First try to find the image in the filesystem
                string sourceFilePath = FindImagePath(imageName);
                if (!string.IsNullOrEmpty(sourceFilePath))
                {
                    var destFilePath = Path.Combine(outputDir, imageName);

                    try
                    {
                        // Ensure the output directory exists
                        Directory.CreateDirectory(Path.GetDirectoryName(destFilePath));

                        // Just copy the file directly - let the user use specialized tools to view it if needed
                        using (var sourceStream = fileSystem.Open(sourceFilePath))
                        using (var destStream = File.Create(destFilePath))
                        {
                            sourceStream.CopyTo(destStream);
                        }

                        Console.WriteLine($"Exported original image: {imageName}");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Warning: Failed to export image {imageName}: {ex.Message}");
                    }
                }

                // If not found in filesystem, try to extract from MIX files
                // Parse template name to determine the tileset
                string tileset = GetTilesetFromTemplateName(imageName);
                if (!string.IsNullOrEmpty(tileset))
                {
                    var templateData = mixLoader.GetTemplateFromMix(tileset, imageName);
                    if (templateData != null && templateData.Length > 0)
                    {
                        // Convert template data to image
                        using var image = templateConverter.ConvertTemplateToImage(templateData);
                        if (image != null)
                        {
                            var destFilePath = Path.Combine(outputDir, Path.ChangeExtension(imageName, ".png"));
                            Directory.CreateDirectory(Path.GetDirectoryName(destFilePath));
                            image.Save(destFilePath);
                            Console.WriteLine($"Exported template image from MIX: {imageName}");
                            return true;
                        }
                    }
                }

                Console.WriteLine($"Warning: Image not found: {imageName}");
                Console.WriteLine("Note: Original template image files (like .tem files) require original game assets.");
                Console.WriteLine("They are typically located in the game's installation directory or a content package.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error exporting image: {ex.Message}");
            }

            return false;
        }

        public Image<Rgba32> LoadTemplateImage(string imageName)
        {
            try
            {
                // First try to find the image in the filesystem
                string sourceFilePath = FindImagePath(imageName);
                if (!string.IsNullOrEmpty(sourceFilePath))
                {
                    // Try to convert the template file to an image
                    try
                    {
                        using (var sourceStream = fileSystem.Open(sourceFilePath))
                        {
                            // For simplicity, we're just checking if it's a standard image format
                            // If it's a game-specific format, we'd need more specialized handling
                            try
                            {
                                // Try to load as a standard image file
                                return Image.Load<Rgba32>(sourceStream);
                            }
                            catch
                            {
                                // It's likely a game-specific format (TEM, DES, etc.)
                                // We'll fall through to the MIX loader below
                                Console.WriteLine($"Warning: Couldn't load {imageName} as a standard image format");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Warning: Failed to load image {imageName} from filesystem: {ex.Message}");
                    }
                }

                // If not found in filesystem or couldn't load as a standard image, try MIX files
                string tileset = GetTilesetFromTemplateName(imageName);
                if (!string.IsNullOrEmpty(tileset))
                {
                    var templateData = mixLoader.GetTemplateFromMix(tileset, imageName);
                    if (templateData != null && templateData.Length > 0)
                    {
                        // Convert template data to image
                        var image = templateConverter.ConvertTemplateToImage(templateData);
                        if (image != null)
                        {
                            Console.WriteLine($"Loaded template image from MIX: {imageName}");
                            return image;
                        }
                    }
                }

                Console.WriteLine($"Warning: Image not found for loading: {imageName}");
                Console.WriteLine("Note: Original template image files (like .tem files) require original game assets.");
                Console.WriteLine("They are typically located in the game's installation directory or a content package.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading template image: {ex.Message}");
            }

            return null;
        }

        private string FindImagePath(string imageName)
        {
            // Use cache to avoid repeated lookups
            if (imagePathCache.TryGetValue(imageName, out var cachedPath))
            {
                return cachedPath;
            }

            // Get all files in the file system
            var allFiles = ((TilesetFileSystem)fileSystem).GetAllFileNames();

            // Find the image file
            var matchingFiles = allFiles
                .Where(f => StringComparer.OrdinalIgnoreCase.Equals(Path.GetFileName(f), imageName))
                .ToList();

            var result = matchingFiles.Any() ? matchingFiles.First() : null;
            imagePathCache[imageName] = result;
            return result;
        }

        private string GetTilesetFromTemplateName(string templateName)
        {
            // Template filenames typically follow a pattern: <tileset>.<template_name>.tem
            // Example: temperat.t01.tem - "temperat" is the tileset
            
            // Special cases for specific tilesets
            if (templateName.StartsWith("d", StringComparison.OrdinalIgnoreCase) && 
                char.IsDigit(templateName[1]))
                return "desert";
                
            if (templateName.StartsWith("s", StringComparison.OrdinalIgnoreCase) && 
                char.IsDigit(templateName[1]))
                return "snow";
                
            if (templateName.StartsWith("t", StringComparison.OrdinalIgnoreCase) && 
                char.IsDigit(templateName[1]))
                return "temperat";
                
            if (templateName.StartsWith("i", StringComparison.OrdinalIgnoreCase) && 
                char.IsDigit(templateName[1]))
                return "interior";
                
            // Extract from filename if it matches the pattern
            var parts = templateName.Split('.');
            if (parts.Length >= 2)
            {
                var potentialTileset = parts[0].ToLowerInvariant();
                
                // Known tilesets in Red Alert
                string[] knownTilesets = { "desert", "interior", "snow", "temperat" };
                
                if (knownTilesets.Contains(potentialTileset))
                {
                    return potentialTileset;
                }
            }
            
            // If we can't determine, default to "temperat" which is the most common
            return "temperat";
        }
    }
}
