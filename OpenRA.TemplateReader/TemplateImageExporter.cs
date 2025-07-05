using System;
using System.IO;
using System.Linq;
using OpenRA.FileSystem;

namespace OpenRA.TemplateReader
{
    public class TemplateImageExporter
    {
        private readonly string gamePath;
        private readonly IReadOnlyFileSystem fileSystem;

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
                // Get all files in the file system
                var allFiles = ((TilesetFileSystem)fileSystem).GetAllFileNames();

                // Find the image file
                var matchingFiles = allFiles
                    .Where(f => Path.GetFileName(f).Equals(imageName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (matchingFiles.Any())
                {
                    var sourceFilePath = matchingFiles.First();
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
                else
                {
                    Console.WriteLine($"Warning: Image not found: {imageName}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error exporting image: {ex.Message}");
            }

            return false;
        }
    }
}
