using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenRA.FileSystem;
using OpenRA.Primitives;

namespace OpenRA.TemplateReader
{
    public class TemplateReader
    {
        private readonly string gamePath;
        private readonly IReadOnlyFileSystem fileSystem;

        public TemplateReader(string gamePath)
        {
            this.gamePath = gamePath;

            // Create file system to access mod files
            var modDataLoader = new ModDataLoader();
            var modData = modDataLoader.CreateFolderMods(new[] { gamePath });
            fileSystem = modData.ModFiles;
        }

        public TemplateExportInfo ReadTemplate(string tilesetName, ushort templateId)
        {
            // Normalize tileset name
            var normalizedName = tilesetName.ToLowerInvariant();

            // Get all files in the file system
            var allFiles = ((TilesetFileSystem)fileSystem).GetAllFileNames();

            Console.WriteLine($"Looking for template {templateId} in tileset '{normalizedName}'");

            // First look for YAML tileset file which might contain the template definition
            var yamlTilesetPath = allFiles.FirstOrDefault(f =>
                f.Contains("tilesets/") &&
                Path.GetFileNameWithoutExtension(f).Equals(normalizedName, StringComparison.OrdinalIgnoreCase) &&
                Path.GetExtension(f).Equals(".yaml", StringComparison.OrdinalIgnoreCase));

            TemplateExportInfo template = null;

            if (!string.IsNullOrEmpty(yamlTilesetPath))
            {
                Console.WriteLine($"Found YAML tileset file: {yamlTilesetPath}");
                try
                {
                    template = ReadTemplateFromYaml(yamlTilesetPath, templateId, normalizedName);
                    if (template != null)
                    {
                        Console.WriteLine($"Found template {templateId} in YAML file");
                        return template;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Error reading YAML tileset file: {ex.Message}");
                    // Continue with fallback method
                }
            }

            // Try to read template directly from a template file
            var templateExtension = GetTilesetExtension(normalizedName);
            var templateFileName = $"t{templateId:D2}{templateExtension}";
            
            var templateFiles = allFiles.Where(f => 
                Path.GetFileName(f).Equals(templateFileName, StringComparison.OrdinalIgnoreCase));

            var templateFilePath = templateFiles.FirstOrDefault();

            if (!string.IsNullOrEmpty(templateFilePath))
            {
                Console.WriteLine($"Found template file: {templateFilePath}");
                try
                {
                    using (var stream = fileSystem.Open(templateFilePath))
                    {
                        template = ReadTemplateFile(stream, templateId, normalizedName);
                        if (template != null)
                        {
                            Console.WriteLine($"Successfully read template {templateId} from file");
                            return template;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Error reading template file: {ex.Message}");
                }
            }
            else
            {
                // Try alternate template file naming formats if the standard one isn't found
                Console.WriteLine($"Standard template file '{templateFileName}' not found, trying alternatives...");
                
                // Look for other possible template files
                var alternateTemplateFiles = allFiles.Where(f => 
                    Path.GetExtension(f).Equals(templateExtension, StringComparison.OrdinalIgnoreCase) &&
                    (f.Contains($"{templateId}") || f.Contains($"{templateId:D3}")));
                
                foreach (var alternateFile in alternateTemplateFiles)
                {
                    Console.WriteLine($"Trying alternate template file: {alternateFile}");
                    try
                    {
                        using (var stream = fileSystem.Open(alternateFile))
                        {
                            template = ReadTemplateFile(stream, templateId, normalizedName);
                            if (template != null)
                            {
                                // Update the image name to match the actual file
                                template.Images = new[] { Path.GetFileName(alternateFile) };
                                Console.WriteLine($"Successfully read template {templateId} from alternate file");
                                return template;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Warning: Error reading alternate template file: {ex.Message}");
                    }
                }
            }

            // If we still haven't found the template, create a placeholder
            if (template == null)
            {
                Console.WriteLine($"Template {templateId} not found, creating placeholder");
                template = CreateBasicTemplate(templateId, normalizedName);
            }

            return template;
        }

        private TemplateExportInfo ReadTemplateFromYaml(string yamlPath, ushort templateId, string tilesetName)
        {
            try
            {
                Console.WriteLine($"Reading YAML tileset: {yamlPath} for template {templateId}");

                // Read YAML file as text
                string yamlContent;
                using (var stream = fileSystem.Open(yamlPath))
                using (var reader = new StreamReader(stream))
                {
                    yamlContent = reader.ReadToEnd();
                }

                // Use OpenRA's MiniYaml to parse the file
                var yaml = MiniYaml.FromString(yamlContent, yamlPath);
                
                // Create list of terrain types for reference
                var terrainTypes = new List<TerrainTypeInfo>();
                
                // Process the Terrain section to get terrain types
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

                                terrainTypes.Add(terrainType);
                                Console.WriteLine($"  Added terrain type: {terrainType.Name}");
                            }
                        }
                    }
                }
                
                // If no terrain types were found, add some defaults
                if (terrainTypes.Count == 0)
                {
                    for (byte i = 0; i < 10; i++)
                    {
                        terrainTypes.Add(new TerrainTypeInfo
                        {
                            Index = i,
                            Name = $"Terrain{i}",
                            IsPassable = true
                        });
                    }
                }

                // Find the specific template we're looking for
                var templatesNode = yaml.FirstOrDefault(n => n.Key == "Templates");
                if (templatesNode != null)
                {
                    foreach (var node in templatesNode.Value.Nodes)
                    {
                        if (node.Key.StartsWith("Template@"))
                        {
                            // Get template ID
                            var idNode = node.Value.Nodes.FirstOrDefault(n => n.Key == "Id");
                            if (idNode != null && ushort.TryParse(idNode.Value.Value, out var id) && id == templateId)
                            {
                                // Found our template!
                                Console.WriteLine($"  Found template {templateId} in YAML");

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
                                    var extension = GetTilesetExtension(tilesetName);
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
                                            var terrainType = terrainTypes.FirstOrDefault(t => t.Name == terrainTypeName);
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

                                // Return the template if it has tiles
                                if (template.Tiles.Count > 0)
                                {
                                    return template;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading YAML tileset: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }

            return null;
        }

        private TemplateExportInfo ReadTemplateFile(Stream stream, ushort templateId, string tilesetName)
        {
            try
            {
                // Debug file size
                var fileLength = stream.Length;
                Console.WriteLine($"Template file size: {fileLength} bytes");

                if (fileLength < 2)
                {
                    Console.WriteLine($"Template file too small");
                    return null;
                }

                // Template file format (simplification of actual format):
                // - First byte: template width
                // - Second byte: template height
                // - For each tile in the template:
                //   - 1 byte: terrain type
                //   - 1 byte: height/z-level
                var width = TemplateStreamExts.ReadUInt8(stream);
                var height = TemplateStreamExts.ReadUInt8(stream);

                // Validate width and height - if they're unreasonable, use defaults
                if (width == 0 || width > 64 || height == 0 || height > 64)
                {
                    Console.WriteLine($"Invalid template dimensions: {width}x{height}");
                    return null;
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
                        var terrainType = TemplateStreamExts.ReadUInt8(stream);
                        var tileHeight = TemplateStreamExts.ReadUInt8(stream);

                        // Check for ramp info - in some formats, the height byte encodes both height and ramp type
                        var rampType = (byte)0;
                        if ((tileHeight & 0x80) != 0)  // If high bit is set, it's a ramp
                        {
                            rampType = (byte)((tileHeight >> 4) & 0x07);
                            tileHeight = (byte)(tileHeight & 0x0F);
                        }

                        template.Tiles.Add(new TemplateTileExportInfo
                        {
                            Index = index,
                            TerrainType = terrainType,
                            Height = tileHeight,
                            RampType = rampType,
                            // Set reasonable color ranges
                            MinColor = new[] { 100, 100, 100, 255 },
                            MaxColor = new[] { 200, 200, 200, 255 }
                        });
                    }
                }

                // Set images to be extracted based on template ID
                var extension = GetTilesetExtension(tilesetName);
                template.Images = new[] { $"t{templateId:D2}{extension}" };

                return template;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Error reading template {templateId}: {ex.Message}");
                return null;
            }
        }

        private TemplateExportInfo CreateBasicTemplate(ushort templateId, string tilesetName)
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
            var extension = GetTilesetExtension(tilesetName);
            template.Images = new[] { $"t{templateId:D2}{extension}" };

            return template;
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
