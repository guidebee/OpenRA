using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using OpenRA.FileSystem;
using OpenRA.Primitives;

namespace OpenRA.MapReader
{
    public class MapReader
    {
        public MapJsonFormat ReadMap(string mapPath)
        {
            // Check if the path is a folder
            if (Directory.Exists(mapPath))
            {
                var package = new Folder(mapPath);
                return ReadMapPackage(package);
            }
            
            // Check if the path is a zip file
            if (File.Exists(mapPath) && Path.GetExtension(mapPath).ToLowerInvariant() == ".zip")
            {
                // Create a custom ZipFile implementation that wraps ZipArchive
                using (var stream = File.OpenRead(mapPath))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    var zipPackage = new ZipFileWrapper(archive);
                    return ReadMapPackage(zipPackage);
                }
            }
            
            throw new FileNotFoundException($"Map not found at path: {mapPath}");
        }
        
        private MapJsonFormat ReadMapPackage(IReadOnlyPackage package)
        {
            // Check if required files exist
            if (!package.Contains("map.yaml") || !package.Contains("map.bin"))
                throw new InvalidDataException("Not a valid map: missing map.yaml or map.bin");
            
            // Parse the map.yaml to get metadata
            var mapYaml = new MiniYaml(null, MiniYaml.FromStream(package.GetStream("map.yaml"), package.Name + ":map.yaml"));
            
            // Parse format and basic data
            var format = mapYaml.NodeWithKeyOrDefault("MapFormat")?.Value.Value;
            if (format == null || !int.TryParse(format, out var mapFormat))
                throw new InvalidDataException("Invalid or missing MapFormat in map.yaml");
            
            var title = mapYaml.NodeWithKeyOrDefault("Title")?.Value.Value ?? "Untitled";
            var author = mapYaml.NodeWithKeyOrDefault("Author")?.Value.Value ?? "Unknown";
            var tileset = mapYaml.NodeWithKeyOrDefault("Tileset")?.Value.Value ?? "";
            
            // Parse map size
            var mapSizeNode = mapYaml.NodeWithKeyOrDefault("MapSize")?.Value.Value;
            if (mapSizeNode == null)
                throw new InvalidDataException("Missing MapSize in map.yaml");
            
            var mapSizeParts = mapSizeNode.Split(',');
            if (mapSizeParts.Length != 2 || 
                !int.TryParse(mapSizeParts[0], out var width) || 
                !int.TryParse(mapSizeParts[1], out var height))
                throw new InvalidDataException("Invalid MapSize format in map.yaml");
            
            var mapSize = new int2(width, height);
            
            // Parse bounds
            var boundsNode = mapYaml.NodeWithKeyOrDefault("Bounds")?.Value.Value;
            Rectangle bounds;
            if (boundsNode != null)
            {
                var boundsParts = boundsNode.Split(',');
                if (boundsParts.Length == 4 &&
                    int.TryParse(boundsParts[0], out var left) &&
                    int.TryParse(boundsParts[1], out var top) &&
                    int.TryParse(boundsParts[2], out var right) &&
                    int.TryParse(boundsParts[3], out var bottom))
                {
                    bounds = Rectangle.FromLTRB(left, top, left + right, top + bottom);
                }
                else
                {
                    bounds = new Rectangle(0, 0, width, height);
                }
            }
            else
            {
                bounds = new Rectangle(0, 0, width, height);
            }
            
            // Create the JSON format object
            var mapJson = new MapJsonFormat
            {
                Format = mapFormat,
                Title = title,
                Author = author,
                Tileset = tileset,
                MapSize = mapSize,
                Bounds = bounds
            };
            
            // Read player definitions
            var playerNodes = mapYaml.NodeWithKeyOrDefault("Players")?.Value.Nodes;
            if (playerNodes != null)
            {
                foreach (var playerNode in playerNodes)
                {
                    var playerId = playerNode.Key;
                    var playerData = playerNode.Value;
                    
                    var name = playerData.NodeWithKeyOrDefault("Name")?.Value.Value ?? playerId;
                    var faction = playerData.NodeWithKeyOrDefault("Faction")?.Value.Value ?? "Random";
                    var color = playerData.NodeWithKeyOrDefault("Color")?.Value.Value ?? "";
                    var isHuman = (playerData.NodeWithKeyOrDefault("Playable")?.Value.Value ?? "False") == "True";
                    
                    int team = 0;
                    if (int.TryParse(playerData.NodeWithKeyOrDefault("Team")?.Value.Value ?? "0", out var teamValue))
                        team = teamValue;
                    
                    mapJson.Players[playerId] = new PlayerInfo
                    {
                        Name = name,
                        Faction = faction,
                        Color = color,
                        IsHuman = isHuman,
                        Team = team
                    };
                }
            }
            
            // Read actor definitions
            var actorNodes = mapYaml.NodeWithKeyOrDefault("Actors")?.Value.Nodes;
            if (actorNodes != null)
            {
                foreach (var actorNode in actorNodes)
                {
                    var actorId = actorNode.Key;
                    var actorData = actorNode.Value;
                    
                    var type = actorData.Value;
                    if (string.IsNullOrEmpty(type))
                        continue;
                    
                    var actorInfo = new ActorInfo
                    {
                        Type = type,
                        Properties = new Dictionary<string, string>()
                    };
                    
                    // Parse actor properties
                    foreach (var propNode in actorData.Nodes)
                    {
                        var propKey = propNode.Key;
                        var propValue = propNode.Value.Value;
                        
                        // Check for location property
                        if (propKey == "Location")
                        {
                            if (TryParseCPos(propValue, out var location))
                                actorInfo.Location = location;
                        }
                        // Check for owner property
                        else if (propKey == "Owner")
                        {
                            actorInfo.Owner = propValue;
                        }
                        // Store other properties
                        else
                        {
                            actorInfo.Properties[propKey] = propValue;
                        }
                    }
                    
                    mapJson.Actors[actorId] = actorInfo;
                    
                    // Detect spawn points and associate them with players
                    if (type == "mpspawn" && actorInfo.Owner != null && actorInfo.Location != default)
                    {
                        if (mapJson.Players.TryGetValue(actorInfo.Owner, out var player))
                        {
                            player.SpawnPoint = actorInfo.Location;
                        }
                    }
                }
            }
            
            // Track the cells with multiple tiles for Z-order calculation
            var cellTileCount = new Dictionary<(int X, int Y), int>();
            
            // Now read the binary map data
            using (var stream = package.GetStream("map.bin"))
            {
                if (stream == null)
                    throw new InvalidDataException("Could not read map.bin");
                
                var header = ReadBinaryHeader(stream, mapSize);
                
                // Read tiles
                if (header.TilesOffset > 0)
                {
                    stream.Position = header.TilesOffset;
                    for (var i = 0; i < mapSize.X; i++)
                    {
                        for (var j = 0; j < mapSize.Y; j++)
                        {
                            // Read tile type and index
                            var tileBytes = new byte[3];
                            stream.Read(tileBytes, 0, 3);
                            
                            // The tile type is a ushort (2 bytes), stored in little-endian format
                            var tileType = BitConverter.ToUInt16(tileBytes, 0);
                            var tileIndex = tileBytes[2];
                            
                            // Count tiles per cell for Z-order calculation
                            var key = (i, j);
                            if (!cellTileCount.ContainsKey(key))
                                cellTileCount[key] = 0;
                            cellTileCount[key]++;
                            
                            // Track template information
                            if (!mapJson.Templates.ContainsKey(tileType))
                            {
                                mapJson.Templates[tileType] = new TilesetTemplate
                                {
                                    Id = tileType,
                                    Name = $"Template{tileType}",
                                    Size = new int2(1, 1), // Assume 1x1 since we don't have actual size info
                                    Tiles = new List<TemplateTileInfo>()
                                };
                            }
                            
                            // Add the tile index to the template if not already there
                            var template = mapJson.Templates[tileType];
                            if (!template.Tiles.Any(t => t.Index == tileIndex))
                            {
                                template.Tiles.Add(new TemplateTileInfo
                                {
                                    Index = tileIndex,
                                    TerrainType = 0, // Unknown without actual tileset data
                                    Height = 0      // Unknown without actual tileset data
                                });
                            }
                            
                            // Add tile with Z-order (the later it's processed, the higher the Z-order)
                            mapJson.Tiles.Add(new TileInfo
                            {
                                X = i,
                                Y = j,
                                Type = tileType,
                                Index = tileIndex,
                                ZOrder = cellTileCount[key] - 1  // 0-based index for z-order
                            });
                        }
                    }
                }
                
                // Read heights if present
                if (header.HeightsOffset > 0)
                {
                    stream.Position = header.HeightsOffset;
                    for (var i = 0; i < mapSize.X; i++)
                    {
                        for (var j = 0; j < mapSize.Y; j++)
                        {
                            var heightValue = stream.ReadUInt8();
                            
                            mapJson.Heights.Add(new HeightInfo
                            {
                                X = i,
                                Y = j,
                                Height = heightValue
                            });
                        }
                    }
                }
                
                // Read resources if present
                if (header.ResourcesOffset > 0)
                {
                    stream.Position = header.ResourcesOffset;
                    for (var i = 0; i < mapSize.X; i++)
                    {
                        for (var j = 0; j < mapSize.Y; j++)
                        {
                            var type = stream.ReadUInt8();
                            var density = stream.ReadUInt8();
                            
                            if (type > 0)
                            {
                                mapJson.Resources.Add(new ResourceInfo
                                {
                                    X = i,
                                    Y = j,
                                    Type = type,
                                    Density = density
                                });
                            }
                        }
                    }
                }
            }
            
            return mapJson;
        }
        
        private static BinaryHeader ReadBinaryHeader(Stream stream, int2 mapSize)
        {
            var format = stream.ReadUInt8();
            var width = stream.ReadUInt16();
            var height = stream.ReadUInt16();
            
            if (width != mapSize.X || height != mapSize.Y)
                throw new InvalidDataException("Map size in map.bin does not match map.yaml");
            
            if (format == 1)
            {
                return new BinaryHeader
                {
                    Format = format,
                    TilesOffset = 5,
                    HeightsOffset = 0,
                    ResourcesOffset = (uint)(3 * width * height + 5)
                };
            }
            else if (format == 2)
            {
                return new BinaryHeader
                {
                    Format = format,
                    TilesOffset = stream.ReadUInt32(),
                    HeightsOffset = stream.ReadUInt32(),
                    ResourcesOffset = stream.ReadUInt32()
                };
            }
            
            throw new InvalidDataException($"Unknown binary map format '{format}'");
        }
        
        private static bool TryParseCPos(string s, out CPos pos)
        {
            pos = default;
            
            var parts = s.Split(',');
            if (parts.Length >= 2 && 
                int.TryParse(parts[0], out var x) && 
                int.TryParse(parts[1], out var y))
            {
                pos = new CPos(x, y);
                return true;
            }
            
            return false;
        }
        
        private class BinaryHeader
        {
            public byte Format { get; set; }
            public uint TilesOffset { get; set; }
            public uint HeightsOffset { get; set; }
            public uint ResourcesOffset { get; set; }
        }
        
        // Custom ZipFile wrapper that implements IReadOnlyPackage
        private class ZipFileWrapper : IReadOnlyPackage
        {
            private readonly ZipArchive archive;
            
            public ZipFileWrapper(ZipArchive archive)
            {
                this.archive = archive;
            }
            
            public string Name => "MapArchive";
            
            public IEnumerable<string> Contents => GetContentsList();
            
            private IEnumerable<string> GetContentsList()
            {
                foreach (var entry in archive.Entries)
                    yield return entry.FullName.Replace('\\', '/');
            }
            
            public bool Contains(string filename)
            {
                return archive.GetEntry(filename.Replace('/', '\\')) != null;
            }
            
            public Stream GetStream(string filename)
            {
                var entry = archive.GetEntry(filename.Replace('/', '\\'));
                if (entry == null)
                    return null;
                
                // Create a memory stream that we can return
                var ms = new MemoryStream();
                using (var entryStream = entry.Open())
                    entryStream.CopyTo(ms);
                
                ms.Position = 0;
                return ms;
            }
            
            public IReadOnlyPackage OpenPackage(string filename, FileSystem.FileSystem context = null)
            {
                // Maps typically don't have nested packages, but we need to implement this method
                // for the interface. Return null to indicate no package was found.
                return null;
            }
            
            public void Dispose()
            {
                // The ZipArchive will be disposed by the caller
            }
        }
    }
}
