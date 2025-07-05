using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenRA.FileSystem;

namespace OpenRA.TemplateReader
{
    public class MixLoader
    {
        private readonly Dictionary<string, List<MixFile>> tilesetMixFiles = new Dictionary<string, List<MixFile>>();
        private readonly Dictionary<string, byte[]> extractedFileCache = new Dictionary<string, byte[]>();

        public MixLoader()
        {
            LoadMixFiles();
        }

        private void LoadMixFiles()
        {
            // Get the OpenRA support directory
            string supportDir = GetSupportDir();
            if (string.IsNullOrEmpty(supportDir))
                return;

            // Look for RA Content directory
            string raContentDir = Path.Combine(supportDir, "Content", "ra", "v2");
            if (!Directory.Exists(raContentDir))
            {
                Console.WriteLine($"Warning: RA content directory not found at {raContentDir}");
                return;
            }

            // Load all .mix files relevant to tilesets
            LoadMixFile(raContentDir, "temperat.mix", "temperat");
            LoadMixFile(raContentDir, "winter.mix", "snow");
            LoadMixFile(raContentDir, "snow.mix", "snow");
            LoadMixFile(raContentDir, "interior.mix", "interior");
            LoadMixFile(raContentDir, Path.Combine("cnc", "desert.mix"), "desert");
        }

        public string GetSupportDir()
        {
            string supportDir = null;

            // Windows: %APPDATA%\OpenRA
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                supportDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenRA");
            }
            // Linux: ~/.openra
            else if (Environment.OSVersion.Platform == PlatformID.Unix)
            {
                supportDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), ".openra");
            }
            // macOS: ~/Library/Application Support/OpenRA
            else if (Environment.OSVersion.Platform == PlatformID.MacOSX)
            {
                supportDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Personal),
                    "Library", "Application Support", "OpenRA");
            }

            if (string.IsNullOrEmpty(supportDir) || !Directory.Exists(supportDir))
            {
                Console.WriteLine("Warning: OpenRA support directory not found");
                return null;
            }

            return supportDir;
        }

        public List<string> GetAvailableMixFiles()
        {
            var result = new List<string>();
            string supportDir = GetSupportDir();
            if (string.IsNullOrEmpty(supportDir))
                return result;

            string raContentDir = Path.Combine(supportDir, "Content", "ra", "v2");
            if (!Directory.Exists(raContentDir))
                return result;

            try
            {
                // Get all MIX files in the RA content directory
                var mixFiles = Directory.GetFiles(raContentDir, "*.mix", SearchOption.AllDirectories);
                result.AddRange(mixFiles.Select(Path.GetFileName));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error listing MIX files: {ex.Message}");
            }

            return result;
        }

        public byte[] GetTemplateFromMix(string tileset, string templateName)
        {
            // Template files typically have extensions matching the tileset
            // For example: temperat.t01.tem, desert.t01.des, snow.t01.sno, etc.
            string extension = GetTemplateExtension(tileset);
            
            // Try various naming patterns
            string[] possibleNames = new[]
            {
                templateName,                                // Direct match (if templateName already has extension)
                $"{templateName}{extension}",                // Simple template name with extension
                $"{tileset}.{templateName}",                 // With tileset prefix
                $"{tileset}.{templateName}{extension}",      // Full pattern with tileset prefix and extension
                $"{templateName.Replace(extension, "")}{extension}" // Fix double extension
            };

            foreach (var name in possibleNames)
            {
                byte[] data = ExtractFile(tileset, name);
                if (data != null && data.Length > 0)
                {
                    Console.WriteLine($"Found template {name} in {tileset} MIX file");
                    return data;
                }
            }

            Console.WriteLine($"Warning: Template {templateName} not found in any {tileset} MIX file");
            return null;
        }

        private string GetTemplateExtension(string tileset)
        {
            // Return the appropriate extension for the given tileset
            switch (tileset.ToLowerInvariant())
            {
                case "temperat": return ".tem";
                case "snow": return ".sno";
                case "desert": return ".des";
                case "interior": return ".int";
                default: return ".tem"; // Default to temperate
            }
        }

        private void LoadMixFile(string baseDir, string mixFileName, string tileset)
        {
            try
            {
                string mixFilePath = Path.Combine(baseDir, mixFileName);
                if (!File.Exists(mixFilePath))
                {
                    Console.WriteLine($"Warning: Mix file not found: {mixFilePath}");
                    return;
                }

                // Load the MIX file
                using (var stream = File.OpenRead(mixFilePath))
                {
                    try
                    {
                        var mixFile = new MixFile(stream);
                        if (!tilesetMixFiles.ContainsKey(tileset))
                            tilesetMixFiles[tileset] = new List<MixFile>();
                        
                        tilesetMixFiles[tileset].Add(mixFile);
                        Console.WriteLine($"Loaded MIX file: {mixFilePath}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error loading MIX file {mixFilePath}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error accessing MIX file {mixFileName}: {ex.Message}");
            }
        }

        public byte[] ExtractFile(string tileset, string filename)
        {
            // Check cache first
            string cacheKey = $"{tileset}:{filename}";
            if (extractedFileCache.TryGetValue(cacheKey, out byte[] cachedData))
                return cachedData;

            if (!tilesetMixFiles.TryGetValue(tileset, out var mixFiles) || mixFiles == null || mixFiles.Count == 0)
            {
                Console.WriteLine($"Warning: No MIX files loaded for tileset {tileset}");
                return null;
            }

            foreach (var mixFile in mixFiles)
            {
                try
                {
                    if (mixFile.Contains(filename))
                    {
                        Console.WriteLine($"Found {filename} in {tileset} MIX file");
                        byte[] data = mixFile.Extract(filename);
                        extractedFileCache[cacheKey] = data; // Cache the result
                        return data;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error extracting {filename} from {tileset} MIX: {ex.Message}");
                }
            }

            Console.WriteLine($"Warning: {filename} not found in any {tileset} MIX file");
            return null;
        }
    }
}
