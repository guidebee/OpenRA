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
            // Try using the standard template finding logic first
            byte[] result = GetTemplateFromMixInternal(tileset, templateName);
            if (result != null && result.Length > 0)
                return result;

            // If not found in the primary tileset, check if this is a special template
            // that might be found in other tilesets
            if (templateName.StartsWith("rv", StringComparison.OrdinalIgnoreCase) ||
                templateName.StartsWith("sh", StringComparison.OrdinalIgnoreCase) ||
                templateName.Contains(".tem"))
            {
                Console.WriteLine($"Template not found in {tileset}, checking alternate tilesets for {templateName}");

                // Try other tilesets
                foreach (var alternateTileset in new[] { "temperat", "snow", "winter", "interior", "desert" })
                {
                    if (alternateTileset != tileset)
                    {
                        result = GetTemplateFromMixInternal(alternateTileset, templateName);
                        if (result != null && result.Length > 0)
                            return result;
                    }
                }

                // Try general mix files
                foreach (var generalTileset in new[] { "general", "local", "conquer" })
                {
                    result = GetTemplateFromMixInternal(generalTileset, templateName);
                    if (result != null && result.Length > 0)
                        return result;
                }
            }

            // If we couldn't find the template, log and return null
            Console.WriteLine($"Template {templateName} not found in any tileset MIX files");
            return null;
        }

        private byte[] GetTemplateFromMixInternal(string tileset, string templateName)
        {
            // Template files typically have extensions matching the tileset
            // For example: temperat.t01.tem, desert.t01.des, snow.t01.sno, etc.
            string extension = GetTemplateExtension(tileset);

            // Try various naming patterns - expanded to cover more possibilities
            var possibleNames = new List<string>
            {
                templateName,                                // Direct match (if templateName already has extension)
                $"{templateName}{extension}",                // Simple template name with extension
                $"{tileset}.{templateName}",                 // With tileset prefix
                $"{tileset}.{templateName}{extension}",      // Full pattern with tileset prefix and extension
                $"{templateName.Replace(extension, "")}{extension}" // Fix double extension
            };

            // Add more template name patterns for special types

            // For template IDs like 115, try different prefix styles
            if (int.TryParse(templateName.Replace(".tem", "").Replace(".sno", "").Replace(".des", "").Replace(".int", ""), out int templateId))
            {
                // Add standard formats t[id], d[id], s[id], etc.
                possibleNames.Add($"t{templateId:D2}{extension}");
                possibleNames.Add($"t{templateId:D3}{extension}");

                // Special cases for river templates (rv prefix)
                if (templateId >= 0 && templateId <= 20)
                {
                    possibleNames.Add($"rv{templateId:D2}{extension}");
                    possibleNames.Add($"rv{templateId:D2}");
                }

                // Special cases for road templates (d prefix)
                if (templateId >= 0 && templateId <= 50)
                {
                    possibleNames.Add($"d{templateId:D2}{extension}");
                    possibleNames.Add($"d{templateId:D2}");
                }
            }

            // For river templates like rv04.tem
            if (templateName.StartsWith("rv", StringComparison.OrdinalIgnoreCase))
            {
                var baseName = templateName.Replace(".tem", "").Replace(".sno", "").Replace(".des", "").Replace(".int", "");
                possibleNames.Add($"{baseName}{extension}");
                possibleNames.Add($"{baseName}");
            }

            // Add debug logging to show the templates we're looking for
            Console.WriteLine($"Looking for template: {templateName} in tileset: {tileset}");
            Console.WriteLine($"Trying these patterns: {string.Join(", ", possibleNames.Take(5))}...");

            foreach (var name in possibleNames)
            {
                byte[] data = ExtractFile(tileset, name);
                if (data != null && data.Length > 0)
                {
                    Console.WriteLine($"Found template {name} in {tileset} MIX file");
                    return data;
                }
            }

            // Special handling for specific templates
            if (templateName.Equals("rv04.tem", StringComparison.OrdinalIgnoreCase) ||
                (templateId == 115 && tileset.Equals("temperat", StringComparison.OrdinalIgnoreCase)))
            {
                // Try some alternative tilesets
                foreach (var alternateTileset in new[] { "temperat", "snow", "winter", "interior", "desert" })
                {
                    if (alternateTileset != tileset)
                    {
                        Console.WriteLine($"Trying alternate tileset {alternateTileset} for rv04.tem");
                        byte[] data = ExtractFile(alternateTileset, "rv04.tem");
                        if (data != null && data.Length > 0)
                        {
                            Console.WriteLine($"Found rv04.tem in {alternateTileset} MIX file");
                            return data;
                        }
                    }
                }

                // Try general mix files with different file naming
                foreach (var generalTileset in new[] { "general", "local", "conquer" })
                {
                    byte[] data = ExtractFile(generalTileset, "rv04.tem");
                    if (data != null && data.Length > 0)
                    {
                        Console.WriteLine($"Found rv04.tem in {generalTileset} MIX file");
                        return data;
                    }
                }
            }

            Console.WriteLine($"Warning: Template {templateName} not found in any {tileset} MIX file");
            return null;

            foreach (var name in possibleNames)
            {
                byte[] data = ExtractFile(tileset, name);
                if (data != null && data.Length > 0)
                {
                    Console.WriteLine($"Found template {name} in {tileset} MIX file");
                    return data;
                }
            }

            // Special handling for specific templates
            if (templateName.Equals("rv04.tem", StringComparison.OrdinalIgnoreCase) ||
                (templateId == 115 && tileset.Equals("temperat", StringComparison.OrdinalIgnoreCase)))
            {
                // Try some alternative tilesets
                foreach (var alternateTileset in new[] { "temperat", "snow", "winter", "interior", "desert" })
                {
                    if (alternateTileset != tileset)
                    {
                        Console.WriteLine($"Trying alternate tileset {alternateTileset} for rv04.tem");
                        byte[] data = ExtractFile(alternateTileset, "rv04.tem");
                        if (data != null && data.Length > 0)
                        {
                            Console.WriteLine($"Found rv04.tem in {alternateTileset} MIX file");
                            return data;
                        }
                    }
                }

                // Try general mix files with different file naming
                foreach (var generalTileset in new[] { "general", "local", "conquer" })
                {
                    byte[] data = ExtractFile(generalTileset, "rv04.tem");
                    if (data != null && data.Length > 0)
                    {
                        Console.WriteLine($"Found rv04.tem in {generalTileset} MIX file");
                        return data;
                    }
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
