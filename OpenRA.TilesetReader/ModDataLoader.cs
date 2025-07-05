using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenRA.FileSystem;

namespace OpenRA.TilesetReader
{
    public class ModDataLoader
    {
        public ModData CreateFolderMods(string[] paths)
        {
            var modData = new ModData();
            var fs = new TilesetFileSystem();

            Console.WriteLine($"Creating folder mods from {paths.Length} paths");

            // Add each path as a folder
            foreach (var path in paths)
            {
                if (Directory.Exists(path))
                {
                    fs.Mount(new Folder(path));
                    Console.WriteLine($"Mounted folder: {path}");
                    
                    // If the path might be a game directory, look for common mod folders
                    TryMountModFolders(fs, path);
                }
                else
                {
                    Console.WriteLine($"Warning: Directory does not exist: {path}");
                }
            }

            // Add current directory as a fallback
            if (!paths.Contains(Environment.CurrentDirectory))
            {
                fs.Mount(new Folder(Environment.CurrentDirectory));
                Console.WriteLine($"Mounted current directory: {Environment.CurrentDirectory}");
                TryMountModFolders(fs, Environment.CurrentDirectory);
            }

            // Print all the mounted folders to help diagnose issues
            Console.WriteLine("Mounted packages:");
            foreach (var package in fs.Packages)
            {
                Console.WriteLine($"  {package.Name}");
            }

            // Print all available files to help diagnose issues
            var allFiles = fs.GetAllFileNames().ToList();
            Console.WriteLine($"Total files available: {allFiles.Count}");

            // Print a sample of files with specific extensions to help diagnose
            var tilesetFiles = allFiles.Where(f => 
                Path.GetExtension(f).Equals(".TIL", StringComparison.OrdinalIgnoreCase) || 
                Path.GetExtension(f).Equals(".tileset", StringComparison.OrdinalIgnoreCase)).ToList();
            
            Console.WriteLine($"Found {tilesetFiles.Count} tileset files:");
            foreach (var file in tilesetFiles.Take(10))
            {
                Console.WriteLine($"  {file}");
            }

            var templateFiles = allFiles.Where(f => 
                Path.GetExtension(f).Equals(".tem", StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(f).Equals(".sno", StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(f).Equals(".des", StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(f).Equals(".int", StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(f).Equals(".jun", StringComparison.OrdinalIgnoreCase)).ToList();
                
            Console.WriteLine($"Found {templateFiles.Count} template files:");
            foreach (var file in templateFiles.Take(10))
            {
                Console.WriteLine($"  {file}");
            }

            modData.ModFiles = fs;
            return modData;
        }
        
        private void TryMountModFolders(TilesetFileSystem fs, string basePath)
        {
            // Try to mount common mod folders if they exist
            var modsPath = Path.Combine(basePath, "mods");
            if (Directory.Exists(modsPath))
            {
                Console.WriteLine($"Found mods directory: {modsPath}");
                
                // Look for RA tilesets and bits folders
                var raModPath = Path.Combine(modsPath, "ra");
                if (Directory.Exists(raModPath))
                {
                    // Mount RA tilesets folder
                    var raTilesetsPath = Path.Combine(raModPath, "tilesets");
                    if (Directory.Exists(raTilesetsPath))
                    {
                        fs.Mount(new Folder(raTilesetsPath));
                        Console.WriteLine($"Mounted folder: {raTilesetsPath}");
                    }
                    
                    // Mount RA bits folder - IMPORTANT for template images
                    var raBitsPath = Path.Combine(raModPath, "bits");
                    if (Directory.Exists(raBitsPath))
                    {
                        fs.Mount(new Folder(raBitsPath));
                        Console.WriteLine($"Mounted folder: {raBitsPath}");
                    }
                }
                
                // Look for CnC tilesets and bits folders
                var cncModPath = Path.Combine(modsPath, "cnc");
                if (Directory.Exists(cncModPath))
                {
                    // Mount CnC tilesets folder
                    var cncTilesetsPath = Path.Combine(cncModPath, "tilesets");
                    if (Directory.Exists(cncTilesetsPath))
                    {
                        fs.Mount(new Folder(cncTilesetsPath));
                        Console.WriteLine($"Mounted folder: {cncTilesetsPath}");
                    }
                    
                    // Mount CnC bits folder
                    var cncBitsPath = Path.Combine(cncModPath, "bits");
                    if (Directory.Exists(cncBitsPath))
                    {
                        fs.Mount(new Folder(cncBitsPath));
                        Console.WriteLine($"Mounted folder: {cncBitsPath}");
                    }
                }
                
                // Look for D2k tilesets and bits folders
                var d2kModPath = Path.Combine(modsPath, "d2k");
                if (Directory.Exists(d2kModPath))
                {
                    // Mount D2k tilesets folder
                    var d2kTilesetsPath = Path.Combine(d2kModPath, "tilesets");
                    if (Directory.Exists(d2kTilesetsPath))
                    {
                        fs.Mount(new Folder(d2kTilesetsPath));
                        Console.WriteLine($"Mounted folder: {d2kTilesetsPath}");
                    }
                    
                    // Mount D2k bits folder
                    var d2kBitsPath = Path.Combine(d2kModPath, "bits");
                    if (Directory.Exists(d2kBitsPath))
                    {
                        fs.Mount(new Folder(d2kBitsPath));
                        Console.WriteLine($"Mounted folder: {d2kBitsPath}");
                    }
                }
                
                // Mount all mod folders as a fallback
                foreach (var modDir in Directory.GetDirectories(modsPath))
                {
                    // Mount tilesets folder
                    var modTilesetsPath = Path.Combine(modDir, "tilesets");
                    if (Directory.Exists(modTilesetsPath))
                    {
                        fs.Mount(new Folder(modTilesetsPath));
                        Console.WriteLine($"Mounted folder: {modTilesetsPath}");
                    }
                    
                    // Mount bits folder
                    var modBitsPath = Path.Combine(modDir, "bits");
                    if (Directory.Exists(modBitsPath))
                    {
                        fs.Mount(new Folder(modBitsPath));
                        Console.WriteLine($"Mounted folder: {modBitsPath}");
                    }
                }
            }
            
            // Check if we're already in a tilesets directory
            if (Path.GetFileName(basePath).Equals("tilesets", StringComparison.OrdinalIgnoreCase))
            {
                // We might be in a mod's tilesets directory, check for parent paths
                var parentDir = Directory.GetParent(basePath)?.FullName;
                if (parentDir != null)
                {
                    // Check for template directories and mount them
                    var templateDirs = Directory.GetDirectories(basePath);
                    foreach (var templateDir in templateDirs)
                    {
                        fs.Mount(new Folder(templateDir));
                        Console.WriteLine($"Mounted template directory: {templateDir}");
                    }
                    
                    // Look for bits directory in the parent (mod directory)
                    var bitsDir = Path.Combine(parentDir, "bits");
                    if (Directory.Exists(bitsDir))
                    {
                        fs.Mount(new Folder(bitsDir));
                        Console.WriteLine($"Mounted bits directory: {bitsDir}");
                    }
                }
            }
            
            // Check if we're already in a mod directory (e.g., ra, cnc, d2k)
            var bitsPath = Path.Combine(basePath, "bits");
            if (Directory.Exists(bitsPath))
            {
                fs.Mount(new Folder(bitsPath));
                Console.WriteLine($"Mounted bits directory: {bitsPath}");
            }
            
            var tilesetsPath = Path.Combine(basePath, "tilesets");
            if (Directory.Exists(tilesetsPath))
            {
                fs.Mount(new Folder(tilesetsPath));
                Console.WriteLine($"Mounted tilesets directory: {tilesetsPath}");
            }
        }
    }

    public class ModData
    {
        public IReadOnlyFileSystem ModFiles { get; set; }
    }

    // Custom implementation of IReadOnlyFileSystem for tileset reading
    public class TilesetFileSystem : IReadOnlyFileSystem
    {
        private readonly List<IReadOnlyPackage> packages = new List<IReadOnlyPackage>();

        public IEnumerable<IReadOnlyPackage> Packages => packages;

        public IEnumerable<string> GetAllFileNames()
        {
            return packages.SelectMany(p => p.Contents);
        }

        public bool Exists(string filename)
        {
            return packages.Any(p => p.Contains(filename));
        }

        public Stream Open(string filename)
        {
            foreach (var package in packages)
            {
                if (package.Contains(filename))
                    return package.GetStream(filename);
            }

            throw new FileNotFoundException($"File not found: {filename}");
        }

        public bool TryOpen(string filename, out Stream stream)
        {
            try
            {
                stream = Open(filename);
                return true;
            }
            catch
            {
                stream = null;
                return false;
            }
        }

        public IReadOnlyPackage OpenPackage(string filename, FileSystem.FileSystem context = null)
        {
            foreach (var package in packages)
            {
                if (package.Contains(filename))
                    return package.OpenPackage(filename, context);
            }

            return null;
        }

        public bool TryGetPackageContaining(string path, out IReadOnlyPackage package, out string filename)
        {
            package = null;
            filename = path;

            foreach (var p in packages)
            {
                if (p.Contains(path))
                {
                    package = p;
                    return true;
                }
            }

            return false;
        }

        public bool IsExternalFile(string filename)
        {
            // For our simplified implementation, assume all files are internal
            return false;
        }

        public void Mount(IReadOnlyPackage package)
        {
            packages.Add(package);
        }
    }
}
