using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenRA.FileSystem;
using OpenRA.Primitives;

namespace OpenRA.TilesetReader
{
    public class TilesetReader
    {
        private readonly string gamePath;
        private readonly IReadOnlyFileSystem fileSystem;

        public TilesetReader(string gamePath)
        {
            this.gamePath = gamePath;

            // Create file system to access mod files
            var modDataLoader = new ModDataLoader();
            var modData = modDataLoader.CreateFolderMods(new[] { gamePath });
            fileSystem = modData.ModFiles;
        }

        public List<string> GetAvailableTilesets()
        {
            // Get all files in the file system
            var allFiles = ((TilesetFileSystem)fileSystem).GetAllFileNames();

            // Find all tileset files (.TIL or .tileset)
            var tilesetFiles = allFiles.Where(f => 
                Path.GetExtension(f).Equals(".TIL", StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(f).Equals(".tileset", StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Extract tileset names from the filenames
            var tilesets = tilesetFiles
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .Select(name => name.ToLowerInvariant())
                .Distinct()
                .ToList();

            // If we have no tilesets, try to guess the common ones based on known template file extensions
            if (!tilesets.Any())
            {
                // Check for template files to guess tilesets
                var extensionsFound = allFiles
                    .Select(f => Path.GetExtension(f).ToLowerInvariant())
                    .Where(ext => ext == ".tem" || ext == ".sno" || ext == ".des" || ext == ".int" || ext == ".jun")
                    .Distinct()
                    .ToList();

                // Map extensions to tileset names
                var extensionToTileset = new Dictionary<string, string>
                {
                    { ".tem", "temperat" },
                    { ".sno", "snow" },
                    { ".des", "desert" },
                    { ".int", "interior" },
                    { ".jun", "jungle" }
                };

                // Add tilesets based on extensions found
                foreach (var ext in extensionsFound)
                {
                    if (extensionToTileset.TryGetValue(ext, out var tilesetName))
                    {
                        tilesets.Add(tilesetName);
                    }
                }
            }

            // If still no tilesets found, add default OpenRA tilesets as a fallback
            if (!tilesets.Any())
            {
                tilesets.AddRange(new[] { "temperat", "snow", "desert", "interior", "jungle" });
                Console.WriteLine("No tileset files found. Using default tilesets as fallback.");
            }

            Console.WriteLine($"Found {tilesets.Count} tilesets: {string.Join(", ", tilesets)}");
            return tilesets;
        }

        public TilesetData ReadTileset(string tilesetName)
        {
            // Normalize tileset name
            var normalizedName = tilesetName.ToUpperInvariant();

            // Get all files in the file system
            var allFiles = ((TilesetFileSystem)fileSystem).GetAllFileNames();

            // Find the tileset file
            var tilesetFiles = allFiles.Where(f =>
                Path.GetFileName(f).Equals($"{normalizedName}.TIL", StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(f).Equals($"{normalizedName}.tileset", StringComparison.OrdinalIgnoreCase));

            var tilesetFilePath = tilesetFiles.FirstOrDefault();

            // Create tileset data structure
            var tilesetData = new TilesetData
            {
                Name = normalizedName,
                Templates = new Dictionary<ushort, TemplateExportInfo>()
            };

            // Try to read the tileset file if found
            if (!string.IsNullOrEmpty(tilesetFilePath))
            {
                Console.WriteLine($"Found tileset file: {tilesetFilePath}");
                try
                {
                    using (var stream = fileSystem.Open(tilesetFilePath))
                    {
                        ReadTilesetFile(stream, tilesetData);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Error reading tileset file: {ex.Message}");
                    // Continue with template processing even if tileset file reading fails
                }
            }
            else
            {
                Console.WriteLine($"Tileset file not found for '{tilesetName}', using template files only");
                // Add default terrain types since we couldn't read them from the tileset
                for (byte i = 0; i < 10; i++)
                {
                    tilesetData.TerrainTypes.Add(new TerrainTypeInfo
                    {
                        Index = i,
                        Name = $"Terrain{i}",
                        IsPassable = true // Default to passable
                    });
                }
            }

            // Find and read associated template files
            var templateFiles = allFiles.Where(f =>
                Path.GetExtension(f).Equals($".{normalizedName.Substring(0, 3)}", StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(f).Equals(".tem", StringComparison.OrdinalIgnoreCase) && normalizedName == "TEMPERAT" ||
                Path.GetExtension(f).Equals(".sno", StringComparison.OrdinalIgnoreCase) && normalizedName == "SNOW" ||
                Path.GetExtension(f).Equals(".des", StringComparison.OrdinalIgnoreCase) && normalizedName == "DESERT" ||
                Path.GetExtension(f).Equals(".int", StringComparison.OrdinalIgnoreCase) && normalizedName == "INTERIOR" ||
                Path.GetExtension(f).Equals(".jun", StringComparison.OrdinalIgnoreCase) && normalizedName == "JUNGLE");

            var templateFilesList = templateFiles.ToList();
            if (templateFilesList.Count == 0)
            {
                throw new FileNotFoundException($"No template files found for tileset '{tilesetName}'");
            }

            // Process template files
            foreach (var templateFile in templateFilesList)
            {
                var filename = Path.GetFileName(templateFile);
                Console.WriteLine($"Processing template file: {filename}");

                // Parse template ID from filename (e.g., "t01.tem" -> 1)
                if (filename.StartsWith("t") && ushort.TryParse(filename.Substring(1, 2), out var templateId))
                {
                    try
                    {
                        using (var stream = fileSystem.Open(templateFile))
                        {
                            ReadTemplateFile(stream, templateId, tilesetData);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Warning: Error reading template {templateId}: {ex.Message}");
                    }
                }
            }

            // Create tileset index
            tilesetData.Index = new TilesetIndexInfo
            {
                Tileset = normalizedName,
                TemplateCount = tilesetData.Templates.Count,
                Templates = tilesetData.Templates.Values.Select(t => new TemplateReference
                {
                    Id = t.Id,
                    Name = $"Template{t.Id}"
                }).ToList()
            };

            return tilesetData;
        }

        private void ReadTilesetFile(Stream stream, TilesetData tilesetData)
        {
            // Read file header
            var format = stream.ReadUInt8();

            if (format != 1)
                throw new InvalidDataException($"Unsupported tileset format: {format}");

            // Read terrain types
            var terrainTypeCount = stream.ReadUInt16();
            for (int i = 0; i < terrainTypeCount; i++)
            {
                var terrainType = new TerrainTypeInfo
                {
                    Index = (byte)i,
                    Name = $"Terrain{i}",
                    IsPassable = true // Default to passable since we don't have real data
                };

                tilesetData.TerrainTypes.Add(terrainType);
            }

            // Skip other tileset data since we're primarily interested in templates
        }

        private void ReadTemplateFile(Stream stream, ushort templateId, TilesetData tilesetData)
        {
            // Template file format (simplification of actual format):
            // - First byte: template width
            // - Second byte: template height
            // - For each tile in the template:
            //   - 1 byte: terrain type
            //   - 1 byte: height/z-level

            try
            {
                var width = stream.ReadUInt8();
                var height = stream.ReadUInt8();

                var template = new TemplateExportInfo
                {
                    Id = templateId,
                    Size = new int2(width, height),
                    PickAny = false,
                    Categories = new[] { "terrain" },
                    Frames = new[] { 0 },
                    Palette = "terrain",
                    Tiles = new List<TemplateTileExportInfo>()
                };

                // Read tile data for each cell in the template
                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        var index = y * width + x;

                        // Some template files might be shorter than expected
                        if (stream.Position >= stream.Length)
                            break;

                        var terrainType = stream.ReadUInt8();
                        var tileHeight = stream.ReadUInt8();

                        // Check for ramp info - in some formats, the height byte encodes both height and ramp type
                        var rampType = (byte)0;
                        if ((tileHeight & 0x80) != 0)  // If high bit is set, it's a ramp
                        {
                            rampType = (byte)(tileHeight >> 4 & 0x07);
                            tileHeight &= 0x0F;
                        }

                        template.Tiles.Add(new TemplateTileExportInfo
                        {
                            Index = index,
                            TerrainType = terrainType,
                            Height = tileHeight,
                            RampType = rampType,
                            // Placeholder values for color ranges (will be calculated during image extraction)
                            MinColor = new[] { 0, 0, 0 },
                            MaxColor = new[] { 255, 255, 255 }
                        });
                    }
                }

                // Set images to be extracted based on template ID
                var extension = GetTilesetExtension(tilesetData.Name);
                template.Images = new[] { $"t{templateId:D2}{extension}" };

                // Add to templates collection
                tilesetData.Templates[templateId] = template;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Error reading template {templateId}: {ex.Message}");
            }
        }

        private string GetTilesetExtension(string tileset)
        {
            return tileset.ToUpperInvariant() switch
            {
                "TEMPERAT" => ".tem",
                "SNOW" => ".sno",
                "DESERT" => ".des",
                "INTERIOR" => ".int",
                "JUNGLE" => ".jun",
                _ => ".tem" // Default to temperate
            };
        }
    }

    public class TilesetData
    {
        public string Name { get; set; }
        public Dictionary<ushort, TemplateExportInfo> Templates { get; set; }
        public List<TerrainTypeInfo> TerrainTypes { get; set; } = new List<TerrainTypeInfo>();
        public TilesetIndexInfo Index { get; set; }
    }

    public class TerrainTypeInfo
    {
        public byte Index { get; set; }
        public string Name { get; set; }
        public bool IsPassable { get; set; }
    }
}
