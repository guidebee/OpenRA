using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenRA.FileSystem;

namespace OpenRA.TemplateReader
{
    // This is a simplified mock of the ModDataLoader class
    public class ModDataLoader
    {
        public ModData CreateFolderMods(string[] folders)
        {
            var modData = new ModData();
            var fs = new TilesetFileSystem(folders);
            modData.ModFiles = fs;
            return modData;
        }
    }

    // This is a simplified mock of the ModData class
    public class ModData
    {
        public IReadOnlyFileSystem ModFiles { get; set; }
    }

    // This is a simplified mock of the TilesetFileSystem class
    public class TilesetFileSystem : IReadOnlyFileSystem
    {
        private readonly string[] basePaths;
        private List<string> cachedFiles;

        public TilesetFileSystem(string[] basePaths)
        {
            this.basePaths = basePaths;
        }

        public bool Exists(string filename)
        {
            foreach (var basePath in basePaths)
            {
                var fullPath = Path.Combine(basePath, filename);
                if (File.Exists(fullPath))
                    return true;
            }
            return false;
        }

        public Stream Open(string filename)
        {
            foreach (var basePath in basePaths)
            {
                var fullPath = Path.Combine(basePath, filename);
                if (File.Exists(fullPath))
                    return File.OpenRead(fullPath);
            }
            
            throw new FileNotFoundException($"File not found: {filename}");
        }

        public bool TryOpen(string filename, out Stream s)
        {
            s = null;
            try
            {
                foreach (var basePath in basePaths)
                {
                    var fullPath = Path.Combine(basePath, filename);
                    if (File.Exists(fullPath))
                    {
                        s = File.OpenRead(fullPath);
                        return true;
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        public bool TryGetPackageContaining(string path, out IReadOnlyPackage package, out string filename)
        {
            package = null;
            filename = path;
            return false;
        }

        public bool IsExternalFile(string filename)
        {
            return false;
        }

        public IEnumerable<string> GetAllFileNames()
        {
            if (cachedFiles != null)
                return cachedFiles;

            cachedFiles = new List<string>();
            
            foreach (var basePath in basePaths)
            {
                try
                {
                    // Look in tilesets directory
                    var tilesetsDir = Path.Combine(basePath, "tilesets");
                    if (Directory.Exists(tilesetsDir))
                    {
                        // Get YAML tileset definitions
                        var yamlFiles = Directory.GetFiles(tilesetsDir, "*.yaml", SearchOption.AllDirectories);
                        cachedFiles.AddRange(yamlFiles.Select(f => f.Substring(basePath.Length + 1).Replace('\\', '/')));
                        
                        // Get template files
                        var templateFiles = Directory.GetFiles(tilesetsDir, "*.tem", SearchOption.AllDirectories)
                            .Concat(Directory.GetFiles(tilesetsDir, "*.des", SearchOption.AllDirectories))
                            .Concat(Directory.GetFiles(tilesetsDir, "*.sno", SearchOption.AllDirectories))
                            .Concat(Directory.GetFiles(tilesetsDir, "*.int", SearchOption.AllDirectories))
                            .Concat(Directory.GetFiles(tilesetsDir, "*.jun", SearchOption.AllDirectories));
                        
                        cachedFiles.AddRange(templateFiles.Select(f => f.Substring(basePath.Length + 1).Replace('\\', '/')));
                    }

                    // Look in bits directory for images
                    var bitsDir = Path.Combine(basePath, "bits");
                    if (Directory.Exists(bitsDir))
                    {
                        var imageFiles = Directory.GetFiles(bitsDir, "*.*", SearchOption.AllDirectories);
                        cachedFiles.AddRange(imageFiles.Select(f => f.Substring(basePath.Length + 1).Replace('\\', '/')));
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error scanning directory {basePath}: {ex.Message}");
                }
            }
            
            return cachedFiles;
        }
    }
}
