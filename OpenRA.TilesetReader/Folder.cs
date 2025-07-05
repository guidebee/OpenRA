using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenRA.FileSystem;

namespace OpenRA.TilesetReader
{
    // Simple implementation of IReadOnlyPackage for folder access
    public class Folder : IReadOnlyPackage
    {
        private readonly string path;
        private readonly List<string> contents;

        public Folder(string path)
        {
            this.path = path;
            
            try
            {
                // Get all files in the directory and subdirectories
                contents = Directory.GetFiles(path, "*", SearchOption.AllDirectories)
                    .Select(p => p.Substring(path.Length).TrimStart(Path.DirectorySeparatorChar))
                    .ToList();
                
                Console.WriteLine($"Loaded folder: {path} with {contents.Count} files");
                
                // Print a sample of the first few files to help with debugging
                if (contents.Count > 0)
                {
                    Console.WriteLine($"Sample files in {path}:");
                    foreach (var file in contents.Take(5))
                    {
                        Console.WriteLine($"  {file}");
                    }
                }
                
                // Check for files with specific extensions
                var tilesetFiles = contents.Where(f => 
                    Path.GetExtension(f).Equals(".TIL", StringComparison.OrdinalIgnoreCase) ||
                    Path.GetExtension(f).Equals(".tileset", StringComparison.OrdinalIgnoreCase)).ToList();
                
                if (tilesetFiles.Count > 0)
                {
                    Console.WriteLine($"Found {tilesetFiles.Count} tileset files in {path}");
                    foreach (var file in tilesetFiles)
                    {
                        Console.WriteLine($"  {file}");
                    }
                }
                
                var templateFiles = contents.Where(f => 
                    Path.GetExtension(f).Equals(".tem", StringComparison.OrdinalIgnoreCase) ||
                    Path.GetExtension(f).Equals(".sno", StringComparison.OrdinalIgnoreCase) ||
                    Path.GetExtension(f).Equals(".des", StringComparison.OrdinalIgnoreCase) ||
                    Path.GetExtension(f).Equals(".int", StringComparison.OrdinalIgnoreCase) ||
                    Path.GetExtension(f).Equals(".jun", StringComparison.OrdinalIgnoreCase)).ToList();
                
                if (templateFiles.Count > 0)
                {
                    Console.WriteLine($"Found {templateFiles.Count} template files in {path}");
                    foreach (var file in templateFiles.Take(10))
                    {
                        Console.WriteLine($"  {file}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Error loading folder contents from {path}: {ex.Message}");
                contents = new List<string>();
            }
        }

        public string Name => path;

        public IEnumerable<string> Contents => contents;

        public bool Contains(string filename)
        {
            // Normalize path separators
            var normalizedFilename = filename.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            
            return contents.Any(f => f.Equals(normalizedFilename, StringComparison.OrdinalIgnoreCase));
        }

        public Stream GetStream(string filename)
        {
            // Normalize path separators
            var normalizedFilename = filename.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            
            var fullPath = Path.Combine(path, normalizedFilename);
            if (!File.Exists(fullPath))
            {
                // Try to find the file with a case-insensitive search
                var matchingFile = contents.FirstOrDefault(f => f.Equals(normalizedFilename, StringComparison.OrdinalIgnoreCase));
                if (matchingFile != null)
                {
                    fullPath = Path.Combine(path, matchingFile);
                }
                else
                {
                    throw new FileNotFoundException($"File not found: {filename}");
                }
            }

            Console.WriteLine($"Opening file: {fullPath}");
            return File.OpenRead(fullPath);
        }

        public IReadOnlyPackage OpenPackage(string filename, FileSystem.FileSystem context)
        {
            // For simplicity, we don't support nested packages
            return null;
        }

        public void Dispose()
        {
            // Nothing to dispose
        }
    }
}
