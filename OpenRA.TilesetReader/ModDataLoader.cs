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
            }

            // Add current directory as a fallback
            if (!paths.Contains(Environment.CurrentDirectory))
            {
                fs.Mount(new Folder(Environment.CurrentDirectory));
                TryMountModFolders(fs, Environment.CurrentDirectory);
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
                // Look for RA tilesets folder
                var raTilesetsPath = Path.Combine(modsPath, "ra", "tilesets");
                if (Directory.Exists(raTilesetsPath))
                {
                    fs.Mount(new Folder(raTilesetsPath));
                    Console.WriteLine($"Mounted folder: {raTilesetsPath}");
                }
                
                // Look for CnC tilesets folder
                var cncTilesetsPath = Path.Combine(modsPath, "cnc", "tilesets");
                if (Directory.Exists(cncTilesetsPath))
                {
                    fs.Mount(new Folder(cncTilesetsPath));
                    Console.WriteLine($"Mounted folder: {cncTilesetsPath}");
                }
                
                // Look for D2k tilesets folder
                var d2kTilesetsPath = Path.Combine(modsPath, "d2k", "tilesets");
                if (Directory.Exists(d2kTilesetsPath))
                {
                    fs.Mount(new Folder(d2kTilesetsPath));
                    Console.WriteLine($"Mounted folder: {d2kTilesetsPath}");
                }
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
