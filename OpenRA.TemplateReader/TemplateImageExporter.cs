using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenRA.FileSystem;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
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
                Console.WriteLine($"Loading template image: {imageName}");

                // First check if this is a template ID reference (like Template@115)
                if (imageName.StartsWith("Template@", StringComparison.OrdinalIgnoreCase))
                {
                    // Extract the template ID number
                    var templateId = imageName.Substring("Template@".Length);

                    if (ushort.TryParse(templateId, out ushort id))
                    {
                        // This follows the DefaultTileCache pattern from OpenRA.Mods.Common
                        // Load the template data from the template's image file

                        // Get the image filename for this template ID
                        var templateImageFile = FindTemplateImageFile(id);
                        Console.WriteLine($"Template@{id} uses image file: {templateImageFile}");

                        if (!string.IsNullOrEmpty(templateImageFile))
                        {
                            // Look up size from template data or use default
                            int width = 4;
                            int height = 4;

                            // Create a template info with the proper image reference
                            var templateInfo = new TemplateInfo
                            {
                                Id = id,
                                Images = new[] { templateImageFile },
                                Size = new int2(width, height),
                                Categories = new[] { "Unknown" }
                            };

                            // Use the template converter to render the template using the image data
                            return templateConverter.ConvertTemplateToImage(templateInfo,
                                name => LoadTemplateImageData(name));
                        }
                    }
                }

                // If it's not a template ID or we couldn't find the image file,
                // try direct file lookup

                // First check if the file exists in the filesystem
                string sourceFilePath = FindImagePath(imageName);
                if (!string.IsNullOrEmpty(sourceFilePath))
                {
                    try
                    {
                        using (var sourceStream = fileSystem.Open(sourceFilePath))
                        {
                            try
                            {
                                // Try to load as a standard image file
                                return Image.Load<Rgba32>(sourceStream);
                            }
                            catch
                            {
                                // It's likely a game-specific format, try to load from raw data
                                byte[] data;
                                using (var ms = new MemoryStream())
                                {
                                    sourceStream.CopyTo(ms);
                                    data = ms.ToArray();
                                }

                                return templateConverter.ConvertTemplateToImage(data);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Warning: Failed to load image {imageName} from filesystem: {ex.Message}");
                    }
                }

                // If we get here, try to load from MIX files
                return LoadTemplateImageFromMix(imageName);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading template image: {ex.Message}");
            }

            // If all attempts fail, create a placeholder image
            Console.WriteLine($"Creating placeholder for missing image: {imageName}");
            return CreatePlaceholderImage(imageName);
        }

        private Image<Rgba32> LoadTemplateImageFromMix(string imageName)
        {
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
            if (imageName.StartsWith("rv", StringComparison.OrdinalIgnoreCase))
            {
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
            return null;
        }

        private byte[] LoadTemplateImageData(string imageName)
        {
            // Try different tilesets in this order of preference:
            string[] tilesetPreference = {
                GetTilesetFromTemplateName(imageName),
                "temperat",
                "snow",
                "desert",
                "interior",
                "general",
                "local",
                "conquer",
                "hires"
            };

            foreach (var tileset in tilesetPreference)
            {
                if (string.IsNullOrEmpty(tileset)) continue;

                var templateData = mixLoader.GetTemplateFromMix(tileset, imageName);
                if (templateData != null && templateData.Length > 0)
                {
                    Console.WriteLine($"Found template data for {imageName} in {tileset} MIX");
                    return templateData;
                }
            }

            // Log that we couldn't find the template data
            Console.WriteLine($"Warning: Could not find template data for {imageName} in any MIX file");
            return null;
        }

        // Add a method to explicitly handle a template loaded from a YAML file
        public Image<Rgba32> LoadTemplateFromYaml(string yamlContent)
        {
            try
            {
                // Parse the YAML to extract the template information
                var templateInfo = ParseTemplateYaml(yamlContent);
                if (templateInfo == null)
                {
                    Console.WriteLine("Failed to parse template YAML");
                    return null;
                }

                // Use the template info to load and render the template
                return templateConverter.ConvertTemplateToImage(templateInfo,
                    imageName => LoadTemplateImageData(imageName));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading template from YAML: {ex.Message}");
                return null;
            }
        }

        // Create a placeholder image for when the original image can't be loaded
        private Image<Rgba32> CreatePlaceholderImage(string imageName)
        {
            // Default size for the placeholder
            int width = 4;
            int height = 4;
            ushort templateId = 0;

            // If this is a Template@X reference, extract the ID
            if (imageName.StartsWith("Template@", StringComparison.OrdinalIgnoreCase))
            {
                var idPart = imageName.Substring("Template@".Length);
                if (ushort.TryParse(idPart, out ushort id))
                {
                    templateId = id;
                    
                    // Get the expected image file for this template
                    var expectedImageFile = FindTemplateImageFile(id);
                    Console.WriteLine($"Template@{id} would normally use image: {expectedImageFile} but it wasn't found in MIX files");
                    Console.WriteLine($"This may be because you don't have the original game content installed,");
                    Console.WriteLine($"or the file is missing from your installation.");
                    
                    // Special case for known templates
                    if (id == 115)
                    {
                        Console.WriteLine($"Template@115 is a river template that should use rv04.tem.");
                        Console.WriteLine($"Creating a river-like placeholder instead.");
                        width = 4;
                        height = 4;
                    }
                    // Handle other special template types if needed
                }
            }

            // For river templates like rv04.tem - use more appropriate size
            if (imageName.StartsWith("rv", StringComparison.OrdinalIgnoreCase) ||
                (templateId >= 100 && templateId < 120))
            {
                width = 4;
                height = 4;
                Console.WriteLine($"Creating a river-like placeholder for {imageName}");
            }

            return templateConverter.CreatePlaceholderTemplate(width, height, templateId);
        }

        // Method to find a template image file based on template ID
        // This follows the pattern from DefaultTerrainTemplateInfo in OpenRA.Mods.Common
        private string FindTemplateImageFile(ushort templateId)
        {
            // Common naming patterns:
            // - Small IDs (0-99): Usually t{nn}.tem  (e.g., t01.tem, t42.tem)
            // - River templates: rv{nn}.tem (e.g., rv04.tem)
            // - Desert: d{nn}.tem
            // - Specific templates might have specific names

            // Check for known mappings first
            switch (templateId)
            {
                case 115:
                    // Based on game data, Template@115 uses rv04.tem
                    return "rv04.tem";

                // Add more known mappings as you discover them
                // case 101: return "sh01.tem"; // Example of a shore template
            }

            // For river templates
            if (templateId >= 100 && templateId < 120)
            {
                // River templates often use rv{nn}.tem naming
                int riverIndex = templateId - 100;
                if (riverIndex >= 0 && riverIndex <= 20)
                {
                    return $"rv{riverIndex:D2}.tem";
                }
            }

            // For standard templates (fallback)
            return $"t{templateId:D3}.tem";
        }

        private TemplateInfo ParseTemplateYaml(string yamlContent)
        {
            try
            {
                // Basic YAML parsing for the template format
                var lines = yamlContent.Split('\n');
                var templateInfo = new TemplateInfo();
                string id = null;

                string[] images = null;
                int width = 1, height = 1;
                string[] categories = null;
                var tiles = new Dictionary<int, string>();

                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();

                    // Skip empty lines and comments
                    if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith("#"))
                        continue;

                    // Parse ID
                    if (trimmedLine.StartsWith("Id:"))
                    {
                        id = trimmedLine.Substring("Id:".Length).Trim();
                        if (ushort.TryParse(id, out ushort idValue))
                            templateInfo.Id = idValue;
                    }
                    // Parse Images
                    else if (trimmedLine.StartsWith("Images:"))
                    {
                        var imageValue = trimmedLine.Substring("Images:".Length).Trim();
                        images = new[] { imageValue };
                    }
                    // Parse Size
                    else if (trimmedLine.StartsWith("Size:"))
                    {
                        var sizeValue = trimmedLine.Substring("Size:".Length).Trim();
                        var parts = sizeValue.Split(',');
                        if (parts.Length == 2 &&
                            int.TryParse(parts[0], out width) &&
                            int.TryParse(parts[1], out height))
                        {
                            // Size will be set later
                        }
                    }
                    // Parse Categories
                    else if (trimmedLine.StartsWith("Categories:"))
                    {
                        var categoriesValue = trimmedLine.Substring("Categories:".Length).Trim();
                        categories = new[] { categoriesValue };
                    }
                    // Parse Tiles section
                    else if (trimmedLine.StartsWith("Tiles:"))
                    {
                        // Tiles will be parsed in subsequent lines
                    }
                    // Parse individual tile entries
                    else if (trimmedLine.Contains(":"))
                    {
                        var parts = trimmedLine.Split(':');
                        if (parts.Length == 2 && int.TryParse(parts[0].Trim(), out int tileIndex))
                        {
                            tiles[tileIndex] = parts[1].Trim();
                        }
                    }
                }

                // Set the parsed values
                if (images != null)
                    templateInfo.Images = images;

                templateInfo.Size = new int2(width, height);

                if (categories != null)
                    templateInfo.Categories = categories;

                templateInfo.Tiles = tiles;

                return templateInfo;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing template YAML: {ex.Message}");
                return null;
            }
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

            // Check for template IDs (like Template@115)
            if (templateName.StartsWith("template@", StringComparison.OrdinalIgnoreCase))
            {
                // Extract the ID and use our template lookup
                var idPart = templateName.Substring("template@".Length);
                if (ushort.TryParse(idPart, out ushort id))
                {
                    // Get the actual image file for this template ID
                    var imageFile = FindTemplateImageFile(id);

                    // If we found an image file, determine its tileset
                    if (!string.IsNullOrEmpty(imageFile))
                    {
                        // Recursively call to get the tileset for the actual image
                        return GetTilesetFromTemplateName(imageFile);
                    }
                }

                // Default to temperat for template IDs
                return "temperat";
            }

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
                        case 'r': return "temperat"; // For river templates (rv)
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
            }

            // For template id numbers, default to temperat
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
