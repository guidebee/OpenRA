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
            return contents.Any(f => f.Equals(filename, StringComparison.OrdinalIgnoreCase));
        }

        public Stream GetStream(string filename)
        {
            var fullPath = Path.Combine(path, filename);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"File not found: {filename}");

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
