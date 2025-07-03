using OpenRA.FileSystem;
using OpenRA.Primitives;
using System.IO;

namespace OpenRA.MapReader
{
    /// <summary>
    /// Represents a complete generated map
    /// </summary>
    public class Map
    {
        // Map metadata
        public string Title { get; set; }
        public string Author { get; set; }
        public string TileSet { get; set; }
        public int MapSize { get; set; }
        public int PlayerCount { get; set; }
        
        // Map data
        public Dictionary<CPos, TerrainTile> Tiles { get; set; } = new();
        public Dictionary<string, ActorReference> Actors { get; set; } = new();
        public Dictionary<string, MiniYaml> Rules { get; set; } = new();
        
        // Preview image
        public byte[] PreviewImage { get; set; }
        
        public Map(string title, string tileSet, int mapSize)
        {
            Title = title;
            TileSet = tileSet;
            MapSize = mapSize;
            Author = "OpenRA.MapReader";
            PlayerCount = 2;
        }
        
        /// <summary>
        /// Saves the map to the specified directory
        /// </summary>
        public void Save(string outputPath)
        {
            // Create directory if it doesn't exist
            if (!Directory.Exists(outputPath))
                Directory.CreateDirectory(outputPath);
            
            // Save map.yaml
            SaveMapYaml(Path.Combine(outputPath, "map.yaml"));
            
            // Save binary map data
            SaveMapBin(Path.Combine(outputPath, "map.bin"));
            
            // Save preview image
            SavePreviewImage(Path.Combine(outputPath, "map.png"));
        }
        
        private void SaveMapYaml(string filePath)
        {
            // Generate the map.yaml content using OpenRA's MiniYaml format
            var yaml = new List<MiniYamlNode>
            {
                new MiniYamlNode("MapFormat", new MiniYaml("12")),
                new MiniYamlNode("RequiresMod", new MiniYaml(GetModId())),
                new MiniYamlNode("Title", new MiniYaml(Title)),
                new MiniYamlNode("Author", new MiniYaml(Author)),
                new MiniYamlNode("Tileset", new MiniYaml(TileSet)),
                new MiniYamlNode("MapSize", new MiniYaml($"{MapSize},{MapSize}")),
                new MiniYamlNode("Bounds", new MiniYaml($"1,1,{MapSize-2},{MapSize-2}")),
            };
            
            // Add players section
            yaml.Add(GeneratePlayersSection());
            
            // Add actors section
            yaml.Add(GenerateActorsSection());
            
            // Add rules section if needed
            if (Rules.Count > 0)
            {
                yaml.Add(new MiniYamlNode("Rules", new MiniYaml("", Rules.Select(r => 
                    new MiniYamlNode(r.Key, r.Value)).ToList())));
            }
            
            // Write to file
            using (var writer = new StreamWriter(filePath))
            {
                foreach (var node in yaml)
                {
                    node.WriteTo(writer);
                    writer.WriteLine();
                }
            }
        }
        
        private MiniYamlNode GeneratePlayersSection()
        {
            var players = new List<MiniYamlNode>();
            
            // Add neutral player (world owner)
            players.Add(new MiniYamlNode("PlayerReference@Neutral", new MiniYaml("", new List<MiniYamlNode>
            {
                new MiniYamlNode("Name", new MiniYaml("Neutral")),
                new MiniYamlNode("OwnsWorld", new MiniYaml("True")),
                new MiniYamlNode("NonCombatant", new MiniYaml("True")),
                new MiniYamlNode("Faction", new MiniYaml("random"))
            })));
            
            // Add actual players
            for (int i = 0; i < PlayerCount; i++)
            {
                players.Add(new MiniYamlNode($"PlayerReference@Player{i}", new MiniYaml("", new List<MiniYamlNode>
                {
                    new MiniYamlNode("Name", new MiniYaml($"Player{i+1}")),
                    new MiniYamlNode("Playable", new MiniYaml("True")),
                    new MiniYamlNode("Required", new MiniYaml(i == 0 ? "True" : "False")),
                    new MiniYamlNode("Faction", new MiniYaml("random"))
                })));
            }
            
            return new MiniYamlNode("Players", new MiniYaml("", players));
        }
        
        private MiniYamlNode GenerateActorsSection()
        {
            var actorNodes = new List<MiniYamlNode>();
            
            // Add all actors
            foreach (var actor in Actors)
            {
                actorNodes.Add(new MiniYamlNode(actor.Key, actor.Value.Save()));
            }
            
            return new MiniYamlNode("Actors", new MiniYaml("", actorNodes));
        }
        
        private void SaveMapBin(string filePath)
        {
            // Create a binary writer for the map data
            using (var fs = File.Create(filePath))
            using (var writer = new BinaryWriter(fs))
            {
                // Format version
                writer.Write((uint)1);
                
                // Size
                writer.Write((ushort)MapSize);
                writer.Write((ushort)MapSize);
                
                // Tiles (1-based index for compatibility with the engine)
                for (int y = 0; y < MapSize; y++)
                {
                    for (int x = 0; x < MapSize; x++)
                    {
                        var pos = new CPos(x, y);
                        if (Tiles.TryGetValue(pos, out var tile))
                            writer.Write(tile.ToUInt32());
                        else
                            writer.Write((uint)0); // Default empty tile
                    }
                }
            }
        }
        
        private void SavePreviewImage(string filePath)
        {
            if (PreviewImage != null && PreviewImage.Length > 0)
            {
                File.WriteAllBytes(filePath, PreviewImage);
            }
            else
            {
                // Generate a simple preview image
                using (var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(MapSize, MapSize))
                {
                    // Draw a placeholder image (black with grid)
                    for (int y = 0; y < MapSize; y++)
                    {
                        for (int x = 0; x < MapSize; x++)
                        {
                            var pos = new CPos(x, y);
                            if (Tiles.TryGetValue(pos, out var tile))
                            {
                                // Determine color based on terrain type
                                var color = GetColorForTile(tile);
                                image[x, y] = new SixLabors.ImageSharp.PixelFormats.Rgba32(
                                    color.R, color.G, color.B, color.A);
                            }
                            else
                            {
                                // Default to black
                                image[x, y] = SixLabors.ImageSharp.PixelFormats.Rgba32.Black;
                            }
                        }
                    }
                    
                    image.Save(filePath);
                }
            }
        }
        
        private SixLabors.ImageSharp.PixelFormats.Rgba32 GetColorForTile(TerrainTile tile)
        {
            // Map different terrain types to colors
            // This is a simplified version for preview purposes
            switch (tile.Type)
            {
                case 0: // Clear
                    return new SixLabors.ImageSharp.PixelFormats.Rgba32(76, 230, 0, 255);
                case 1: // Water
                    return new SixLabors.ImageSharp.PixelFormats.Rgba32(0, 160, 255, 255);
                case 2: // Rock
                    return new SixLabors.ImageSharp.PixelFormats.Rgba32(170, 120, 70, 255);
                case 3: // Resource
                    return new SixLabors.ImageSharp.PixelFormats.Rgba32(255, 215, 0, 255);
                default:
                    return new SixLabors.ImageSharp.PixelFormats.Rgba32(128, 128, 128, 255);
            }
        }
        
        private string GetModId()
        {
            // Determine mod ID based on tileset
            switch (TileSet.ToLowerInvariant())
            {
                case "desert":
                case "arrakis":
                    return "d2k";
                case "snow":
                case "temperate":
                case "interior":
                    return "ra";
                case "jungle":
                    return "cnc";
                default:
                    return "ra"; // Default to RA
            }
        }
    }
}
