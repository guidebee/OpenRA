using System.Numerics;

namespace OpenRA.MapGenerator
{
    /// <summary>
    /// Represents a terrain template that can be placed on the map
    /// </summary>
    public class TerrainTemplate
    {
        public string Name { get; set; }
        public int Id { get; set; }
        public string[] Categories { get; set; }
        public int Size { get; set; }
        public Dictionary<int, Tile> Tiles { get; set; } = new();
        public int StartBorderN { get; set; }
        public int EndBorderN { get; set; }
        public int StartTypeN { get; set; }
        public int EndTypeN { get; set; }
        public int MovesX { get; set; }
        public int MovesY { get; set; }
        public List<Vector2> Shape { get; set; } = new();
        
        public TerrainTemplate(string name, int id)
        {
            Name = name;
            Id = id;
            Categories = Array.Empty<string>();
        }
    }

    /// <summary>
    /// Represents a single terrain tile
    /// </summary>
    public class Tile
    {
        public int Index { get; set; }
        public string Type { get; set; }
        public int Height { get; set; }
        public string? Color { get; set; }

        public Tile(int index, string type, int height = 0)
        {
            Index = index;
            Type = type;
            Height = height;
        }
    }

    /// <summary>
    /// Stores information about a terrain path point
    /// </summary>
    public class PathPoint
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int DirectionMask { get; set; }
        public int ReversedDirectionMask { get; set; }

        public PathPoint(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    /// <summary>
    /// Stores information about a path on the terrain
    /// </summary>
    public class TerrainPath
    {
        public List<Vector2> Points { get; set; } = new();
        public string Type { get; set; }
        public string? StartType { get; set; }
        public string? EndType { get; set; }
        public int StartDirN { get; set; }
        public int EndDirN { get; set; }
        public bool IsLoop { get; set; }

        public TerrainPath(string type)
        {
            Type = type;
        }
    }

    /// <summary>
    /// Represents an entity on the map (structure, resource, spawn point, etc.)
    /// </summary>
    public class Entity
    {
        public string Type { get; set; }
        public string Owner { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
    }
}
