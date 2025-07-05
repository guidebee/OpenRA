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

        public TemplateImageExporter(string gamePath)
        {
            this.gamePath = gamePath;

            // Create file system to access mod files
            var modDataLoader = new ModDataLoader();
            var modData = modDataLoader.CreateFolderMods(new[] { gamePath });
            fileSystem = modData.ModFiles;
        }

        public bool ExportImage(string imageName, string outputDir)
        {
            try
            {
                string sourceFilePath = FindImagePath(imageName);
                if (string.IsNullOrEmpty(sourceFilePath))
                {
                    Console.WriteLine($"Warning: Image not found: {imageName}");
                    return false;
                }

                var destFilePath = Path.Combine(outputDir, imageName);

                try
                {
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
                string sourceFilePath = FindImagePath(imageName);
                if (string.IsNullOrEmpty(sourceFilePath))
                {
                    Console.WriteLine($"Warning: Image not found for loading: {imageName}");
                    return null;
                }

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
                            // For now, return null and let the calling code use the procedural renderer
                            Console.WriteLine($"Warning: Couldn't load {imageName} as a standard image format");
                            return null;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Failed to load image {imageName}: {ex.Message}");
                }
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
    }
}
