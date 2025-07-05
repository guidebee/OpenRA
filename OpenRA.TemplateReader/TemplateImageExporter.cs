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
                // Try different tilesets in this order of preference:
                string[] tilesetPreference = {
                    GetTilesetFromTemplateName(imageName), // First try the tileset determined from the filename
                    "temperat",  // Default tileset
                    "snow",      // Other tilesets to try
                    "desert",
                    "interior",
                    "general",   // General mix files
                    "local",     // Other mix files that might contain templates
                    "conquer",
                    "hires"
                };

                foreach (var tileset in tilesetPreference)
                {
                    if (string.IsNullOrEmpty(tileset)) continue;

                    var templateData = mixLoader.GetTemplateFromMix(tileset, imageName);
                    if (templateData != null && templateData.Length > 0)
                    {
                        // Save the raw template data first
                        try
                        {
                            var rawFilePath = Path.Combine(outputDir, imageName);
                            Directory.CreateDirectory(Path.GetDirectoryName(rawFilePath));
                            File.WriteAllBytes(rawFilePath, templateData);
                            Console.WriteLine($"Exported raw template data: {imageName}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Warning: Failed to export raw template data {imageName}: {ex.Message}");
                        }

                        // Convert template data to image
                        using var image = templateConverter.ConvertTemplateToImage(templateData);
                        if (image != null)
                        {
                            var destFilePath = Path.Combine(outputDir, Path.ChangeExtension(imageName, ".png"));
                            Directory.CreateDirectory(Path.GetDirectoryName(destFilePath));
                            image.Save(destFilePath);
                            Console.WriteLine($"Exported template image from {tileset} MIX: {imageName}");
                            return true;
                        }
                    }
                }

                Console.WriteLine($"Warning: Image not found: {imageName}");
                Console.WriteLine("Note: Original template image files (like .tem files) require original game assets.");
                Console.WriteLine("They are typically located in the game's installation directory or a content package.");
                Console.WriteLine("Failed to export original image: " + imageName);
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error exporting image: {ex.Message}");
                return false;
            }
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
                // Try different tilesets in this order of preference:
                string[] tilesetPreference = {
                    GetTilesetFromTemplateName(imageName), // First try the tileset determined from the filename
                    "temperat",  // Default tileset
                    "snow",      // Other tilesets to try
                    "desert",
                    "interior",
                    "general",   // General mix files
                    "local",     // Other mix files that might contain templates
                    "conquer",
                    "hires"
                };
                
                // Special case for river templates (rv*.tem)
                if (imageName.StartsWith("rv", StringComparison.OrdinalIgnoreCase) ||
                    (imageName.Equals("115", StringComparison.OrdinalIgnoreCase) && 
                     tilesetPreference[0].Equals("temperat", StringComparison.OrdinalIgnoreCase)))
                {
                    // Try searching specifically for rv04.tem in all mix files
                    if (imageName.Equals("rv04.tem", StringComparison.OrdinalIgnoreCase) || 
                        imageName.Equals("115", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var tileset in tilesetPreference)
                        {
                            if (string.IsNullOrEmpty(tileset)) continue;
                            
                            var templateData = mixLoader.GetTemplateFromMix(tileset, "rv04.tem");
                            if (templateData != null && templateData.Length > 0)
                            {
                                // Convert template data to image
                                var image = templateConverter.ConvertTemplateToImage(templateData);
                                if (image != null)
                                {
                                    Console.WriteLine($"Loaded template image for rv04.tem from {tileset} MIX");
                                    return image;
                                }
                            }
                        }
                    }
                }

                foreach (var tileset in tilesetPreference)
                {
                    if (string.IsNullOrEmpty(tileset)) continue;

                    var templateData = mixLoader.GetTemplateFromMix(tileset, imageName);
                    if (templateData != null && templateData.Length > 0)
                    {
                        // Convert template data to image
                        var image = templateConverter.ConvertTemplateToImage(templateData);
                        if (image != null)
                        {
                            Console.WriteLine($"Loaded template image from {tileset} MIX: {imageName}");
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
            if (string.IsNullOrEmpty(templateName))
                return "temperat"; // Default to temperate

            // Some templates have a specific naming pattern that indicates the tileset
            // Template names like sh14.tem = shore 14 = temperate
            // Template names like "cliffsl1.tem" = cliff slopes = temperate

            // For template 188 in temperat, the file could be named in various ways
            // e.g., t188.tem, temperat.t188.tem, t188, etc.

            // First check for known prefixes
            templateName = templateName.ToLowerInvariant();

            // Check for explicit prefixes
            if (templateName.StartsWith("temperat") || templateName.StartsWith("temp"))
                return "temperat";

            if (templateName.StartsWith("desert") || templateName.StartsWith("des"))
                return "desert";

            if (templateName.StartsWith("snow") || templateName.StartsWith("winter"))
                return "snow";

            if (templateName.StartsWith("interior") || templateName.StartsWith("int"))
                return "interior";

            // Check for single letter prefixes with digits
            if (templateName.Length >= 2)
            {
                char prefix = templateName[0];
                bool hasDigit = templateName.Length > 1 && char.IsDigit(templateName[1]);

                if (hasDigit)
                {
                    switch (prefix)
                    {
                        case 't': return "temperat";
                        case 'd': return "desert";
                        case 's': return "snow";
                        case 'i': return "interior";
                    }
                }
            }

            // Check for special template types
            if (templateName.Contains("cliff") ||
                templateName.Contains("shore") ||
                templateName.Contains("sh") ||
                templateName.Contains("bridge") ||
                templateName.Contains("road") || 
                templateName.Contains("rv"))  // River templates use rv prefix
                return "temperat";

            if (templateName.Contains("ice") ||
                templateName.Contains("sno"))
                return "snow";

            if (templateName.Contains("des"))
                return "desert";

            // Extract from filename if it matches the pattern x.y.z
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
            }            // For template id numbers, default to temperat
            if (templateName.All(c => char.IsDigit(c)))
                return "temperat";
                
            // Special cases for known templates
            if (templateName.Equals("rv04.tem", StringComparison.OrdinalIgnoreCase))
                return "temperat";
            
            // If we can't determine, default to "temperat" which is the most common
            return "temperat";
        }
    }
}
