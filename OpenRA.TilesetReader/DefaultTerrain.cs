using OpenRA.Graphics;
using OpenRA.Primitives;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenRA.TilesetReader
{
    /// <summary>
    /// Simple implementation of ITerrainInfo for template rendering
    /// </summary>
    public class DefaultTerrain : ITerrainInfo
    {
        private readonly string id;
        private readonly TerrainTypeInfo[] terrainTypes;
        private readonly Dictionary<ushort, TerrainTemplateInfo> templates = new();
        
        public DefaultTerrain(string tilesetId, ModData modData)
        {
            id = tilesetId;
            
            // Create default terrain types
            terrainTypes = new TerrainTypeInfo[10];
            for (byte i = 0; i < terrainTypes.Length; i++)
            {
                terrainTypes[i] = new TerrainTypeInfo
                {
                    Type = i,
                    TargetTypes = new BitSet<string>(),
                    Colors = new[] { new Color(100 + i * 10, 100 + i * 5, 100 - i * 5) }
                };
            }
        }
        
        string ITerrainInfo.Id => id;
        
        TerrainTypeInfo[] ITerrainInfo.TerrainTypes => terrainTypes;
        
        TerrainTile ITerrainInfo.DefaultTerrainTile => new(0, 0);
        
        float ITerrainInfo.MinHeightColorBrightness => 1.0f;
        
        float ITerrainInfo.MaxHeightColorBrightness => 1.0f;
        
        Color ITerrainInfo.GetColor(TerrainTile tile)
        {
            // Return a color based on the tile type for visualization
            if (tile.Type < terrainTypes.Length)
                return terrainTypes[tile.Type].GetColor(Game.CosmeticRandom);
            
            return Color.White;
        }
        
        TerrainTemplateInfo ITemplatedTerrainInfo.Templates => throw new NotImplementedException();
        
        TerrainTile ITemplatedTerrainInfo.GetTerrainTileForTemplate(TerrainTile terrainTile)
        {
            return terrainTile;
        }
        
        TerrainTypeInfo ITerrainInfo.GetTerrainInfo(TerrainTile tile)
        {
            var type = tile.Type % terrainTypes.Length;
            return terrainTypes[type];
        }
        
        bool ITerrainInfo.TryGetTerrainInfo(TerrainTile tile, out TerrainTypeInfo info)
        {
            var type = tile.Type % terrainTypes.Length;
            info = terrainTypes[type];
            return true;
        }
        
        byte ITerrainInfo.GetTerrainIndex(string type)
        {
            // Parse type as a number or return 0
            if (byte.TryParse(type, out var index) && index < terrainTypes.Length)
                return index;
                
            return 0;
        }
        
        TerrainTemplateInfo ITemplatedTerrainInfo.GetTemplateInfo(ushort id)
        {
            // Create template on demand
            if (!templates.TryGetValue(id, out var template))
            {
                template = new TerrainTemplateInfo
                {
                    Id = id,
                    Size = new Size(3, 3),
                    PickAny = false,
                    Tiles = Enumerable.Range(0, 9).Select(i => new TerrainTemplateCell
                    {
                        TerrainType = 0,
                        Height = 0
                    }).ToArray()
                };
                templates.Add(id, template);
            }
            
            return template;
        }
        
        bool ITemplatedTerrainInfo.TryGetTemplateInfo(ushort id, out TerrainTemplateInfo info)
        {
            info = ((ITemplatedTerrainInfo)this).GetTemplateInfo(id);
            return true;
        }
        
        ushort ITemplatedTerrainInfo.GetTerrainTemplateIndex(TerrainTile tile)
        {
            return tile.Type;
        }
    }
    
    /// <summary>
    /// Simple implementation of TerrainTypeInfo for template rendering
    /// </summary>
    public class TerrainTypeInfo : ITerrainTypeInfo
    {
        public byte Type { get; set; }
        public string Name => $"Type{Type}";
        public BitSet<string> TargetTypes { get; set; }
        public Color[] Colors { get; set; }
        public float Speed => 1.0f;
        public byte Height => 0;
        public byte RampType => 0;
        public string CustomCursor => null;
        
        public Color GetColor(MersenneTwister random)
        {
            if (Colors == null || Colors.Length == 0)
                return Color.White;
                
            return Colors[random.Next(Colors.Length)];
        }
    }
    
    /// <summary>
    /// Implementation of TerrainTemplateInfo for template rendering
    /// </summary>
    public class TerrainTemplateInfo : ITerrainTemplateInfo
    {
        public ushort Id { get; set; }
        public Size Size { get; set; }
        public bool PickAny { get; set; }
        public TerrainTemplateCell[] Tiles { get; set; }
        
        public int TilesCount => Tiles.Length;
        
        public TerrainTemplateCell this[int index] => Tiles[index];
        
        public bool Contains(int index) => index >= 0 && index < Tiles.Length;
    }
    
    /// <summary>
    /// Implementation of TerrainTemplateCell for template rendering
    /// </summary>
    public class TerrainTemplateCell : ITerrainTemplateCell
    {
        public byte TerrainType { get; set; }
        public byte Height { get; set; }
    }
}
