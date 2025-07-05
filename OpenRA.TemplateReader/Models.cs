using System.Collections.Generic;
using Newtonsoft.Json;
using OpenRA.Primitives;

namespace OpenRA.TemplateReader
{
    /// <summary>
    /// Template information structure for JSON export
    /// </summary>
    public class TemplateExportInfo
    {
        [JsonProperty("id")]
        public ushort Id { get; set; }
        
        [JsonProperty("size")]
        public int2 Size { get; set; }
        
        [JsonProperty("pickAny")]
        public bool PickAny { get; set; }
        
        [JsonProperty("categories")]
        public string[] Categories { get; set; }
        
        [JsonProperty("images")]
        public string[] Images { get; set; }
        
        [JsonProperty("depthImages")]
        public string[] DepthImages { get; set; }
        
        [JsonProperty("frames")]
        public int[] Frames { get; set; }
        
        [JsonProperty("palette")]
        public string Palette { get; set; }
        
        [JsonProperty("tiles")]
        public List<TemplateTileExportInfo> Tiles { get; set; } = new List<TemplateTileExportInfo>();

        // Helper method to get tileset-specific image paths
        public string[] GetTilesetImages(string tileset)
        {
            if (Images == null || Images.Length == 0)
            {
                // Generate default image names based on tileset and template ID
                var extension = GetTilesetExtension(tileset);
                return new[] { $"t{Id:D2}{extension}" };
            }
            
            return Images;
        }
        
        // Helper method to get the appropriate extension for each tileset
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

    /// <summary>
    /// Tile information structure for JSON export
    /// </summary>
    public class TemplateTileExportInfo
    {
        [JsonProperty("index")]
        public int Index { get; set; }
        
        [JsonProperty("terrainType")]
        public byte TerrainType { get; set; }
        
        [JsonProperty("height")]
        public byte Height { get; set; }
        
        [JsonProperty("rampType")]
        public byte RampType { get; set; }
        
        [JsonProperty("minColor")]
        public int[] MinColor { get; set; }
        
        [JsonProperty("maxColor")]
        public int[] MaxColor { get; set; }
    }
    
    /// <summary>
    /// Terrain type information
    /// </summary>
    public class TerrainTypeInfo
    {
        public byte Index { get; set; }
        public string Name { get; set; }
        public bool IsPassable { get; set; }
    }
}
