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

            // Print all files to help with debugging
            Console.WriteLine("All available files in the file system:");
            foreach (var file in allFiles.Take(30))
            {
                Console.WriteLine($"  {file}");
            }

            // First check for YAML files in the tilesets directory
            var yamlTilesetFiles = allFiles.Where(f =>
                f.Contains("tilesets/") &&
                Path.GetExtension(f).Equals(".yaml", StringComparison.OrdinalIgnoreCase))
                .ToList();

            Console.WriteLine($"Found {yamlTilesetFiles.Count} YAML tileset files:");
            foreach (var file in yamlTilesetFiles)
            {
                Console.WriteLine($"  {file}");
            }

            var tilesets = new List<string>();

            // Extract tileset names from YAML files
            if (yamlTilesetFiles.Any())
            {
                foreach (var file in yamlTilesetFiles)
                {
                    var tilesetName = Path.GetFileNameWithoutExtension(file);
                    tilesets.Add(tilesetName.ToLowerInvariant());
                    Console.WriteLine($"Added tileset from YAML: {tilesetName}");
                }
            }
            else
            {
                // Fall back to looking for TIL or tileset files
                var tilesetFiles = allFiles.Where(f =>
                    Path.GetExtension(f).Equals(".TIL", StringComparison.OrdinalIgnoreCase) ||
                    Path.GetExtension(f).Equals(".tileset", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                // Extract tileset names from the filenames
                var tilesetNames = tilesetFiles
                    .Select(f => Path.GetFileNameWithoutExtension(f))
                    .Select(name => name.ToLowerInvariant())
                    .Distinct()
                    .ToList();

                tilesets.AddRange(tilesetNames);

                // If still no tilesets, try to guess from template file extensions
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
            var normalizedName = tilesetName.ToLowerInvariant();

            // Get all files in the file system
            var allFiles = ((TilesetFileSystem)fileSystem).GetAllFileNames();

            // Create tileset data structure
            var tilesetData = new TilesetData
            {
                Name = normalizedName.ToUpperInvariant(),
                Templates = new Dictionary<ushort, TemplateExportInfo>()
            };

            // First look for YAML tileset file
            var yamlTilesetPath = allFiles.FirstOrDefault(f =>
                f.Contains("tilesets/") &&
                Path.GetFileNameWithoutExtension(f).Equals(normalizedName, StringComparison.OrdinalIgnoreCase) &&
                Path.GetExtension(f).Equals(".yaml", StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(yamlTilesetPath))
            {
                Console.WriteLine($"Found YAML tileset file: {yamlTilesetPath}");
                try
                {
                    ReadTilesetYaml(yamlTilesetPath, tilesetData);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Error reading YAML tileset file: {ex.Message}");
                    // Continue with fallback method
                }
            }

            // If no templates were found from YAML, try the binary format
            if (tilesetData.Templates.Count == 0)
            {
                // Find the tileset file
                var tilesetFiles = allFiles.Where(f =>
                    Path.GetFileName(f).Equals($"{normalizedName.ToUpperInvariant()}.TIL", StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileName(f).Equals($"{normalizedName.ToUpperInvariant()}.tileset", StringComparison.OrdinalIgnoreCase));

                var tilesetFilePath = tilesetFiles.FirstOrDefault();

                // Try to read the tileset file if found
                if (!string.IsNullOrEmpty(tilesetFilePath))
                {
                    Console.WriteLine($"Found binary tileset file: {tilesetFilePath}");
                    try
                    {
                        using (var stream = fileSystem.Open(tilesetFilePath))
                        {
                            ReadTilesetFile(stream, tilesetData);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Warning: Error reading binary tileset file: {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine($"Binary tileset file not found for '{tilesetName}', using template files only");
                }

                // Add default terrain types if none exist
                if (tilesetData.TerrainTypes.Count == 0)
                {
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
            }

            // Find and read associated template files
            var templateExtension = GetTilesetExtension(normalizedName);
            var templateFiles = allFiles.Where(f => 
                Path.GetExtension(f).Equals(templateExtension, StringComparison.OrdinalIgnoreCase));

            var templateFilesList = templateFiles.ToList();
            Console.WriteLine($"Found {templateFilesList.Count} template files for tileset '{tilesetName}' with extension '{templateExtension}'");

            // Debug the files found
            foreach (var file in templateFilesList.Take(10))
            {
                Console.WriteLine($"  Template file: {file}");
            }

            // If we have templates from YAML but no template files, we'll use what we have
            if (templateFilesList.Count == 0 && tilesetData.Templates.Count == 0)
            {
                // Create at least one template so we get some output
                Console.WriteLine($"No template files or YAML templates found. Creating a placeholder template.");
                CreatePlaceholderTemplates(tilesetData);
            }
            else if (templateFilesList.Count > 0)
            {
                // Process all template files
                Console.WriteLine($"Processing {templateFilesList.Count} template files...");
                
                // Sort template files to ensure consistent processing order
                var sortedTemplateFiles = templateFilesList
                    .OrderBy(f => Path.GetFileName(f))
                    .ToList();
                
                foreach (var templateFile in sortedTemplateFiles)
                {
                    var filename = Path.GetFileName(templateFile);
                    Console.WriteLine($"Processing template file: {filename}");

                    // Parse template ID from filename (e.g., "t01.tem" -> 1)
                    ushort templateId;
                    if (filename.StartsWith("t", StringComparison.OrdinalIgnoreCase) && 
                        filename.Length >= 3 && 
                        ushort.TryParse(filename.Substring(1, 2), out templateId))
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
                            // Create a basic template even if reading fails
                            CreateBasicTemplate(templateId, tilesetData);
                        }
                    }
                    else
                    {
                        // Try to extract a template ID from other naming formats
                        if (int.TryParse(Path.GetFileNameWithoutExtension(filename), out var numericId))
                        {
                            templateId = (ushort)numericId;
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
                                CreateBasicTemplate(templateId, tilesetData);
                            }
                        }
                        else
                        {
                            Console.WriteLine($"  Skipping file: {filename} (doesn't match template naming pattern)");
                        }
                    }
                }
            }

            // Create tileset index
            tilesetData.Index = new TilesetIndexInfo
            {
                Tileset = tilesetData.Name,
                TemplateCount = tilesetData.Templates.Count,
                Templates = tilesetData.Templates.Values.Select(t => new TemplateReference
                {
                    Id = t.Id,
                    Name = $"Template{t.Id}"
                }).ToList()
            };

            return tilesetData;
        }

        private void ReadTilesetYaml(string yamlPath, TilesetData tilesetData)
        {
            try
            {
                Console.WriteLine($"Reading YAML tileset: {yamlPath}");

                // Read YAML file as text
                string yamlContent;
                using (var stream = fileSystem.Open(yamlPath))
                using (var reader = new StreamReader(stream))
                {
                    yamlContent = reader.ReadToEnd();
                }

                // Use OpenRA's MiniYaml to parse the file
                var yaml = MiniYaml.FromString(yamlContent, yamlPath);

                // Process the General section
                var generalNode = yaml.FirstOrDefault(n => n.Key == "General");
                if (generalNode != null)
                {
                    // Extract name and ID
                    var nameNode = generalNode.Value.Nodes.FirstOrDefault(n => n.Key == "Name");
                    if (nameNode != null)
                    {
                        Console.WriteLine($"  Tileset Name: {nameNode.Value.Value}");
                    }

                    var idNode = generalNode.Value.Nodes.FirstOrDefault(n => n.Key == "Id");
                    if (idNode != null)
                    {
                        tilesetData.Name = idNode.Value.Value;
                        Console.WriteLine($"  Tileset ID: {tilesetData.Name}");
                    }
                }

                // Process the Terrain section
                var terrainNode = yaml.FirstOrDefault(n => n.Key == "Terrain");
                if (terrainNode != null)
                {
                    byte terrainIndex = 0;
                    foreach (var node in terrainNode.Value.Nodes)
                    {
                        if (node.Key.StartsWith("TerrainType@"))
                        {
                            var typeNode = node.Value.Nodes.FirstOrDefault(n => n.Key == "Type");
                            if (typeNode != null)
                            {
                                var terrainType = new TerrainTypeInfo
                                {
                                    Index = terrainIndex++,
                                    Name = typeNode.Value.Value,
                                    IsPassable = true // Default to passable
                                };

                                tilesetData.TerrainTypes.Add(terrainType);
                                Console.WriteLine($"  Added terrain type: {terrainType.Name}");
                            }
                        }
                    }
                }

                // Process the Templates section
                var templatesNode = yaml.FirstOrDefault(n => n.Key == "Templates");
                if (templatesNode != null)
                {
                    foreach (var node in templatesNode.Value.Nodes)
                    {
                        if (node.Key.StartsWith("Template@"))
                        {
                            // Get template ID
                            var idNode = node.Value.Nodes.FirstOrDefault(n => n.Key == "Id");
                            if (idNode == null || !ushort.TryParse(idNode.Value.Value, out var templateId))
                            {
                                Console.WriteLine($"  Warning: Template {node.Key} has invalid or missing Id");
                                continue;
                            }

                            // Create template info
                            var template = new TemplateExportInfo
                            {
                                Id = templateId,
                                Tiles = new List<TemplateTileExportInfo>()
                            };

                            // Get images
                            var imagesNode = node.Value.Nodes.FirstOrDefault(n => n.Key == "Images");
                            if (imagesNode != null)
                            {
                                template.Images = imagesNode.Value.Value.Split(',')
                                    .Select(s => s.Trim())
                                    .ToArray();

                                Console.WriteLine($"  Template {templateId} images: {string.Join(", ", template.Images)}");
                            }
                            else
                            {
                                var extension = GetTilesetExtension(tilesetData.Name);
                                template.Images = new[] { $"t{templateId:D2}{extension}" };
                            }

                            // Get size
                            var sizeNode = node.Value.Nodes.FirstOrDefault(n => n.Key == "Size");
                            if (sizeNode != null)
                            {
                                var sizeParts = sizeNode.Value.Value.Split(',')
                                    .Select(s => int.Parse(s.Trim()))
                                    .ToArray();

                                if (sizeParts.Length == 2)
                                {
                                    template.Size = new int2(sizeParts[0], sizeParts[1]);
                                    Console.WriteLine($"  Template {templateId} size: {template.Size.X}x{template.Size.Y}");
                                }
                            }
                            else
                            {
                                template.Size = new int2(1, 1);
                            }

                            // Get categories
                            var categoriesNode = node.Value.Nodes.FirstOrDefault(n => n.Key == "Categories");
                            if (categoriesNode != null)
                            {
                                template.Categories = categoriesNode.Value.Value.Split(',')
                                    .Select(s => s.Trim())
                                    .ToArray();
                            }
                            else
                            {
                                template.Categories = new[] { "terrain" };
                            }

                            // Get tiles
                            var tilesNode = node.Value.Nodes.FirstOrDefault(n => n.Key == "Tiles");
                            if (tilesNode != null)
                            {
                                foreach (var tileNode in tilesNode.Value.Nodes)
                                {
                                    if (int.TryParse(tileNode.Key, out var tileIndex))
                                    {
                                        // Find the terrain type by name
                                        var terrainTypeName = tileNode.Value.Value.Trim();
                                        var terrainType = tilesetData.TerrainTypes.FirstOrDefault(t => t.Name == terrainTypeName);
                                        byte terrainTypeIndex = 0;

                                        if (terrainType != null)
                                        {
                                            terrainTypeIndex = terrainType.Index;
                                        }

                                        var tile = new TemplateTileExportInfo
                                        {
                                            Index = tileIndex,
                                            TerrainType = terrainTypeIndex,
                                            Height = 0, // Default height
                                            RampType = 0, // Default ramp
                                            MinColor = new[] { 100, 100, 100, 255 },
                                            MaxColor = new[] { 200, 200, 200, 255 }
                                        };

                                        template.Tiles.Add(tile);
                                        Console.WriteLine($"  Added tile {tileIndex} with terrain {terrainTypeName}");
                                    }
                                }
                            }

                            // Add template to collection if it has tiles
                            if (template.Tiles.Count > 0 || template.Size.X * template.Size.Y == 0)
                            {
                                tilesetData.Templates[templateId] = template;
                                Console.WriteLine($"  Added template {templateId} with {template.Tiles.Count} tiles");
                            }
                        }
                    }
                }

                Console.WriteLine($"Completed reading YAML tileset with {tilesetData.Templates.Count} templates");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading YAML tileset: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }

        private void CreateBasicTemplate(ushort templateId, TilesetData tilesetData)
        {
            var template = new TemplateExportInfo
            {
                Id = templateId,
                Size = new int2(1, 1),
                PickAny = false,
                Categories = new[] { "terrain" },
                Frames = new[] { 0 },
                Palette = "terrain",
                Tiles = new List<TemplateTileExportInfo>()
            };

            // Add a single tile
            template.Tiles.Add(new TemplateTileExportInfo
            {
                Index = 0,
                TerrainType = 0,
                Height = 0,
                RampType = 0,
                MinColor = new[] { 100, 100, 100, 255 },
                MaxColor = new[] { 200, 200, 200, 255 }
            });

            // Set images to be extracted
            var extension = GetTilesetExtension(tilesetData.Name);
            template.Images = new[] { $"t{templateId:D2}{extension}" };

            // Add to templates collection
            tilesetData.Templates[templateId] = template;
            
            Console.WriteLine($"Created basic placeholder template {templateId} with size 1x1");
        }

        private void CreatePlaceholderTemplates(TilesetData tilesetData)
        {
            // Create several placeholder templates to ensure we have something to show
            for (ushort i = 1; i <= 5; i++)
            {
                var template = new TemplateExportInfo
                {
                    Id = i,
                    Size = new int2(i == 1 ? 1 : 3, i == 1 ? 1 : 3),
                    PickAny = false,
                    Categories = new[] { "terrain" },
                    Frames = new[] { 0 },
                    Palette = "terrain",
                    Tiles = new List<TemplateTileExportInfo>()
                };

                // Add tiles to the template
                int width = template.Size.X;
                int height = template.Size.Y;

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        // Use different terrain types and heights based on position
                        byte terrainType = (byte)(i % 5);
                        byte tileHeight = (byte)((x + y) % 10);

                        template.Tiles.Add(new TemplateTileExportInfo
                        {
                            Index = y * width + x,
                            TerrainType = terrainType,
                            Height = tileHeight,
                            RampType = 0
                        });
                    }
                }

                // Set image name based on template ID
                var extension = GetTilesetExtension(tilesetData.Name);
                template.Images = new[] { $"t{i:D2}{extension}" };

                // Add to templates collection
                tilesetData.Templates[i] = template;
                Console.WriteLine($"Created placeholder template {i} with size {width}x{height}");
            }
        }

        private void ReadTilesetFile(Stream stream, TilesetData tilesetData)
        {
            try
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
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing tileset file: {ex.Message}. Creating default terrain types.");

                // Add default terrain types
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
        }

        private void ReadTemplateFile(Stream stream, ushort templateId, TilesetData tilesetData)
        {
            try
            {
                // Debug file size
                var fileLength = stream.Length;
                Console.WriteLine($"Template file size: {fileLength} bytes");

                if (fileLength < 2)
                {
                    Console.WriteLine($"Template file too small, creating basic template");
                    CreateBasicTemplate(templateId, tilesetData);
                    return;
                }

                // Template file format (simplification of actual format):
                // - First byte: template width
                // - Second byte: template height
                // - For each tile in the template:
                //   - 1 byte: terrain type
                //   - 1 byte: height/z-level
                var width = stream.ReadUInt8();
                var height = stream.ReadUInt8();

                // Validate width and height - if they're unreasonable, use defaults
                if (width == 0 || width > 64 || height == 0 || height > 64)
                {
                    Console.WriteLine($"Invalid template dimensions: {width}x{height}, using defaults");
                    CreateBasicTemplate(templateId, tilesetData);
                    return;
                }

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
                        if (stream.Position + 1 >= stream.Length)
                        {
                            Console.WriteLine($"Reached end of stream at position {stream.Position}, expected more tile data");
                            break;
                        }

                        // Read terrain type and height
                        var terrainType = stream.ReadUInt8();
                        var tileHeight = stream.ReadUInt8();

                        // Check for ramp info - in some formats, the height byte encodes both height and ramp type
                        var rampType = (byte)0;
                        if ((tileHeight & 0x80) != 0)  // If high bit is set, it's a ramp
                        {
                            rampType = (byte)((tileHeight >> 4) & 0x07);
                            tileHeight = (byte)(tileHeight & 0x0F);
                        }

                        // Validate terrain type
                        if (terrainType >= tilesetData.TerrainTypes.Count)
                        {
                            // Ensure we have enough terrain types
                            while (terrainType >= tilesetData.TerrainTypes.Count)
                            {
                                tilesetData.TerrainTypes.Add(new TerrainTypeInfo
                                {
                                    Index = (byte)tilesetData.TerrainTypes.Count,
                                    Name = $"Terrain{tilesetData.TerrainTypes.Count}",
                                    IsPassable = true
                                });
                            }
                        }

                        template.Tiles.Add(new TemplateTileExportInfo
                        {
                            Index = index,
                            TerrainType = terrainType,
                            Height = tileHeight,
                            RampType = rampType
                        });
                    }
                }

                // Set images to be extracted based on template ID
                var extension = GetTilesetExtension(tilesetData.Name);
                template.Images = new[] { $"t{templateId:D2}{extension}" };

                // Add to templates collection - replace if exists
                tilesetData.Templates[templateId] = template;
                Console.WriteLine($"Successfully read template {templateId} with dimensions {width}x{height} and {template.Tiles.Count} tiles");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Error reading template {templateId}: {ex.Message}");
                CreateBasicTemplate(templateId, tilesetData);
            }
        }

        private string GetTilesetExtension(string tileset)
        {
            if (string.IsNullOrEmpty(tileset))
            {
                return ".tem"; // Default to temperate if no tileset specified
            }

            // Check for short names first, then full names
            var lowerTileset = tileset.ToLowerInvariant();
            
            // Common abbreviated formats
            if (lowerTileset.StartsWith("tem")) return ".tem";
            if (lowerTileset.StartsWith("sno")) return ".sno";
            if (lowerTileset.StartsWith("des")) return ".des";
            if (lowerTileset.StartsWith("int")) return ".int";
            if (lowerTileset.StartsWith("jun")) return ".jun";
            
            // Full names
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
}
