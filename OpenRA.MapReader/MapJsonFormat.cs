using System.Collections.Generic;
using Newtonsoft.Json;
using OpenRA.Primitives;

namespace OpenRA.MapReader
{
    /// <summary>
    /// Represents the JSON format for map data
    /// </summary>
    public class MapJsonFormat
    {
        [JsonProperty("format")]
        public int Format { get; set; }
        
        [JsonProperty("title")]
        public string Title { get; set; }
        
        [JsonProperty("author")]
        public string Author { get; set; }
        
        [JsonProperty("tileset")]
        public string Tileset { get; set; }
        
        [JsonProperty("mapSize")]
        public int2 MapSize { get; set; }
        
        [JsonProperty("bounds")]
        public Rectangle Bounds { get; set; }
        
        [JsonProperty("tiles")]
        public List<TileInfo> Tiles { get; set; } = new List<TileInfo>();
        
        [JsonProperty("resources")]
        public List<ResourceInfo> Resources { get; set; } = new List<ResourceInfo>();
        
        [JsonProperty("heights")]
        public List<HeightInfo> Heights { get; set; } = new List<HeightInfo>();
        
        [JsonProperty("actors")]
        public Dictionary<string, ActorInfo> Actors { get; set; } = new Dictionary<string, ActorInfo>();
        
        [JsonProperty("players")]
        public Dictionary<string, PlayerInfo> Players { get; set; } = new Dictionary<string, PlayerInfo>();
        
        [JsonProperty("templates")]
        public Dictionary<ushort, TilesetTemplate> Templates { get; set; } = new Dictionary<ushort, TilesetTemplate>();
    }
    
    public class TileInfo
    {
        [JsonProperty("x")]
        public int X { get; set; }
        
        [JsonProperty("y")]
        public int Y { get; set; }
        
        [JsonProperty("type")]
        public ushort Type { get; set; }
        
        [JsonProperty("index")]
        public byte Index { get; set; }
        
        [JsonProperty("zOrder")]
        public int ZOrder { get; set; }
    }
    
    public class TilesetTemplate
    {
        [JsonProperty("id")]
        public ushort Id { get; set; }
        
        [JsonProperty("name")]
        public string Name { get; set; }
        
        [JsonProperty("size")]
        public int2 Size { get; set; }
        
        [JsonProperty("tiles")]
        public List<TemplateTileInfo> Tiles { get; set; } = new List<TemplateTileInfo>();
    }
    
    public class TemplateTileInfo
    {
        [JsonProperty("index")]
        public byte Index { get; set; }
        
        [JsonProperty("terrainType")]
        public byte TerrainType { get; set; }
        
        [JsonProperty("height")]
        public byte Height { get; set; }
    }
    
    public class ResourceInfo
    {
        [JsonProperty("x")]
        public int X { get; set; }
        
        [JsonProperty("y")]
        public int Y { get; set; }
        
        [JsonProperty("type")]
        public byte Type { get; set; }
        
        [JsonProperty("density")]
        public byte Density { get; set; }
    }
    
    public class HeightInfo
    {
        [JsonProperty("x")]
        public int X { get; set; }
        
        [JsonProperty("y")]
        public int Y { get; set; }
        
        [JsonProperty("height")]
        public byte Height { get; set; }
    }
    
    public class ActorInfo
    {
        [JsonProperty("type")]
        public string Type { get; set; }
        
        [JsonProperty("location")]
        public CPos Location { get; set; }
        
        [JsonProperty("owner")]
        public string Owner { get; set; }
        
        [JsonProperty("properties")]
        public Dictionary<string, string> Properties { get; set; } = new Dictionary<string, string>();
    }
    
    public class PlayerInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; }
        
        [JsonProperty("faction")]
        public string Faction { get; set; }
        
        [JsonProperty("color")]
        public string Color { get; set; }
        
        [JsonProperty("isHuman")]
        public bool IsHuman { get; set; }
        
        [JsonProperty("team")]
        public int Team { get; set; }
        
        [JsonProperty("spawnPoint")]
        public CPos SpawnPoint { get; set; }
    }
}
