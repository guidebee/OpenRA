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
            if (Directory.Exists(raContentDir))
            {
                // Load all .mix files relevant to tilesets from RA
                LoadMixFile(raContentDir, "temperat.mix", "temperat");
                LoadMixFile(raContentDir, "winter.mix", "snow");
                LoadMixFile(raContentDir, "snow.mix", "snow");
                LoadMixFile(raContentDir, "interior.mix", "interior");
                LoadMixFile(raContentDir, "desert.mix", "desert");

                // Try to load additional mix files that might contain templates
                LoadMixFile(raContentDir, "general.mix", "general");
                LoadMixFile(raContentDir, "local.mix", "local");
                LoadMixFile(raContentDir, "conquer.mix", "conquer");
                LoadMixFile(raContentDir, "hires.mix", "hires");
            }
            else
            {
                Console.WriteLine($"Warning: RA content directory not found at {raContentDir}");
            }

            // Look for CNC Content directory
            string cncContentDir = Path.Combine(supportDir, "Content", "cnc", "v2");
            if (Directory.Exists(cncContentDir))
            {
                // Load CNC tileset mix files
                LoadMixFile(cncContentDir, "desert.mix", "desert");
                LoadMixFile(cncContentDir, "temperat.mix", "temperat");
                LoadMixFile(cncContentDir, "winter.mix", "snow");
                LoadMixFile(cncContentDir, "snow.mix", "snow");
                LoadMixFile(cncContentDir, "interior.mix", "interior");
            }

            // Look for RA2 Content directory (might contain some compatible files)
            string ra2ContentDir = Path.Combine(supportDir, "Content", "ra2");
            if (Directory.Exists(ra2ContentDir))
            {
                // Load RA2 tileset mix files
                LoadMixFile(ra2ContentDir, "temperat.mix", "temperat");
                LoadMixFile(ra2ContentDir, "snow.mix", "snow");
                LoadMixFile(ra2ContentDir, "urban.mix", "urban");
            }

            // Also check for a cnc subdirectory in the RA directory
            string raCncDir = Path.Combine(raContentDir, "cnc");
            if (Directory.Exists(raCncDir))
            {
                LoadMixFile(raCncDir, "desert.mix", "desert");
            }
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
        }        public byte[] GetTemplateFromMix(string tileset, string templateName)
        {
            // Cache key for looking up previously extracted files
            string cacheKey = $"{tileset}:{templateName}";
            if (extractedFileCache.TryGetValue(cacheKey, out byte[] cachedData))
                return cachedData;
                
            Console.WriteLine($"Looking for template: {templateName} in tileset: {tileset}");
            
            // Generate all possible filenames for this template following OpenRA's pattern
            var possibleNames = GeneratePossibleTemplateNames(tileset, templateName);
            
            // Try each filename pattern in the appropriate MIX files
            foreach (var name in possibleNames)
            {
                byte[] data = ExtractFile(tileset, name);
                if (data != null && data.Length > 0)
                {
                    Console.WriteLine($"Found template {name} in {tileset} MIX file");
                    extractedFileCache[cacheKey] = data; // Cache the result
                    return data;
                }
            }
            
            // If not found in primary tileset, check alternatives
            if (IsSpecialTemplate(templateName))
            {
                byte[] result = CheckAlternativeTilesets(tileset, templateName);
                if (result != null)
                {
                    extractedFileCache[cacheKey] = result; // Cache the result
                    return result;
                }
            }

            // Nothing found
            Console.WriteLine($"Template {templateName} not found in any MIX files");
            return null;
        }
        
        /// <summary>
        /// Generates all possible template filenames based on OpenRA's loading patterns
        /// </summary>
        private List<string> GeneratePossibleTemplateNames(string tileset, string templateName)
        {
            string extension = GetTemplateExtension(tileset);
            var possibleNames = new List<string>();
            
            // Basic name variations
            possibleNames.Add(templateName);
            possibleNames.Add($"{templateName}{extension}");
            possibleNames.Add($"{tileset}.{templateName}");
            possibleNames.Add($"{tileset}.{templateName}{extension}");
            
            // Fix cases where the extension might be duplicated
            if (templateName.EndsWith(".tem") || templateName.EndsWith(".sno") || 
                templateName.EndsWith(".des") || templateName.EndsWith(".int"))
            {
                var baseName = templateName.Substring(0, templateName.Length - 4);
                possibleNames.Add($"{baseName}{extension}");
            }
            
            // Handle numeric template IDs
            if (int.TryParse(templateName.Replace(".tem", "").Replace(".sno", "")
                .Replace(".des", "").Replace(".int", ""), out int templateId))
            {
                // Standard ID formats with various padding
                possibleNames.Add($"t{templateId:D2}{extension}");
                possibleNames.Add($"t{templateId:D3}{extension}");
                
                // Special naming patterns
                if (templateId >= 1 && templateId <= 20)
                {
                    possibleNames.Add($"rv{templateId:D2}{extension}"); // River templates
                    possibleNames.Add($"rv{templateId:D2}");
                }
                
                if (templateId >= 1 && templateId <= 50)
                {
                    possibleNames.Add($"d{templateId:D2}{extension}"); // Road templates
                    possibleNames.Add($"d{templateId:D2}");
                    possibleNames.Add($"sh{templateId:D2}{extension}"); // Shore templates
                    possibleNames.Add($"sh{templateId:D2}");
                }
            }
            
            // For river and shore templates, also add variants without extension
            if (templateName.StartsWith("rv", StringComparison.OrdinalIgnoreCase) ||
                templateName.StartsWith("sh", StringComparison.OrdinalIgnoreCase))
            {
                var baseName = templateName.Replace(".tem", "").Replace(".sno", "")
                    .Replace(".des", "").Replace(".int", "");
                possibleNames.Add(baseName);
                possibleNames.Add($"{baseName}{extension}");
            }
            
            // Special case for Template115 which might be referred to as rv04.tem in some cases
            if (templateId == 115 || templateName.Contains("115"))
            {
                possibleNames.Add("rv04.tem");
                possibleNames.Add($"rv04{extension}");
            }
            
            return possibleNames;
        }
        
        /// <summary>
        /// Determines if a template is a special type that might be found in other tilesets
        /// </summary>
        private bool IsSpecialTemplate(string templateName)
        {
            return templateName.StartsWith("rv", StringComparison.OrdinalIgnoreCase) ||
                   templateName.StartsWith("sh", StringComparison.OrdinalIgnoreCase) ||
                   templateName.Contains("115") ||
                   templateName.Contains(".tem");
        }
        
        /// <summary>
        /// Checks alternative tilesets for special templates
        /// </summary>
        private byte[] CheckAlternativeTilesets(string originalTileset, string templateName)
        {
            Console.WriteLine($"Template not found in {originalTileset}, checking alternate tilesets for {templateName}");
            
            // Check other tilesets
            foreach (var alternateTileset in new[] { "temperat", "snow", "winter", "interior", "desert" })
            {
                if (alternateTileset == originalTileset)
                    continue;
                    
                var possibleNames = GeneratePossibleTemplateNames(alternateTileset, templateName);
                foreach (var name in possibleNames)
                {
                    byte[] data = ExtractFile(alternateTileset, name);
                    if (data != null && data.Length > 0)
                    {
                        Console.WriteLine($"Found template {name} in alternate tileset {alternateTileset}");
                        return data;
                    }
                }
            }
            
            // Check general mix files as a last resort
            foreach (var generalTileset in new[] { "general", "local", "conquer" })
            {
                var possibleNames = GeneratePossibleTemplateNames(generalTileset, templateName);
                foreach (var name in possibleNames)
                {
                    byte[] data = ExtractFile(generalTileset, name);
                    if (data != null && data.Length > 0)
                    {
                        Console.WriteLine($"Found template {name} in general MIX file {generalTileset}");
                        return data;
                    }
                }
            }
            
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

                // Additional validation for the desert.mix file
                if (mixFileName.Contains("desert"))
                {
                    try
                    {
                        // Check file size - skip if too small
                        var fileInfo = new FileInfo(mixFilePath);
                        if (fileInfo.Length < 100)
                        {
                            Console.WriteLine($"Warning: Mix file {mixFileName} is too small, skipping");
                            return;
                        }
                    }
                    catch
                    {
                        // If we can't check the file, continue anyway
                    }
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
