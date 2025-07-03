using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace OpenRA.MapReader
{
    /// <summary>
    /// Core map generator class that creates terrain, resources, and entities
    /// </summary>
    public class MapGenerator
    {
        // Direction constants (similar to JS version)
        private const int DIRECTION_R = 0;
        private const int DIRECTION_RD = 1;
        private const int DIRECTION_D = 2;
        private const int DIRECTION_LD = 3;
        private const int DIRECTION_L = 4;
        private const int DIRECTION_LU = 5;
        private const int DIRECTION_U = 6;
        private const int DIRECTION_RU = 7;

        // Generator settings
        public int Size { get; set; } = 96;
        public int Seed { get; set; } = 12345;
        public int Rotations { get; set; } = 2;
        public int Mirror { get; set; } = 0;
        public float Water { get; set; } = 0.5f;
        public float Mountains { get; set; } = 0.1f;
        public float Forests { get; set; } = 0.025f;
        public int TerrainSmoothing { get; set; } = 4;
        public int MinimumThickness { get; set; } = 5;
        public string CustomName { get; set; } = "";
        public float WavelengthScale { get; set; } = 1.0f;
        public int Players { get; set; } = 1;

        // Working objects
        private readonly RandomGenerator _random;
        private readonly NoiseGenerator _noise;
        private readonly Dictionary<string, TerrainTemplate> _templates = new();
        private readonly Dictionary<string, Tile> _terrainTypes = new();
        private readonly Dictionary<string, List<TerrainTemplate>> _templatesByType = new();
        private readonly Dictionary<int, Dictionary<int, int>> _borderTransitions = new();
        
        public MapGenerator()
        {
            Seed = new Random().Next();
            _random = new RandomGenerator(Seed);
            _noise = new NoiseGenerator(_random);
        }
        
        public MapGenerator(int seed)
        {
            Seed = seed;
            _random = new RandomGenerator(seed);
            _noise = new NoiseGenerator(_random);
        }

        /// <summary>
        /// Load terrain templates from a definition file
        /// </summary>
        public void LoadTemplates(string path)
        {
            // In a real implementation, this would parse template definitions from YAML files
            // For this example, we'll create some basic templates programmatically
            
            // Add some terrain types
            _terrainTypes["Water"] = new Tile(0, "Water") { Color = "#0000FF" };
            _terrainTypes["Land"] = new Tile(0, "Land") { Color = "#00FF00" };
            _terrainTypes["Mountain"] = new Tile(0, "Mountain") { Color = "#A0A0A0" };
            _terrainTypes["Beach"] = new Tile(0, "Beach") { Color = "#FFFF00" };
            _terrainTypes["Forest"] = new Tile(0, "Forest") { Color = "#007700" };
            
            // Create template lists by type
            _templatesByType["Coastline"] = new List<TerrainTemplate>();
            _templatesByType["Cliff"] = new List<TerrainTemplate>();
            _templatesByType["Forest"] = new List<TerrainTemplate>();
            
            // Add some simple templates (in a real implementation these would be more complex)
            for (int i = 0; i < 8; i++)
            {
                var coastTemplate = new TerrainTemplate($"coast{i}", i + 1)
                {
                    StartBorderN = i,
                    EndBorderN = (i + 1) % 8,
                    StartTypeN = 0, // Water
                    EndTypeN = 1,   // Land
                    MovesX = 1,
                    MovesY = 0
                };
                
                // Add shape points
                coastTemplate.Shape.Add(new Vector2(0, 0));
                coastTemplate.Shape.Add(new Vector2(1, 0));
                coastTemplate.Shape.Add(new Vector2(0, 1));
                coastTemplate.Shape.Add(new Vector2(1, 1));
                
                _templates[coastTemplate.Name] = coastTemplate;
                _templatesByType["Coastline"].Add(coastTemplate);
                
                // Similarly add cliff templates
                var cliffTemplate = new TerrainTemplate($"cliff{i}", i + 100)
                {
                    StartBorderN = i,
                    EndBorderN = (i + 1) % 8,
                    StartTypeN = 1, // Land
                    EndTypeN = 2,   // Mountain
                    MovesX = 1,
                    MovesY = 0
                };
                
                cliffTemplate.Shape.Add(new Vector2(0, 0));
                cliffTemplate.Shape.Add(new Vector2(1, 0));
                cliffTemplate.Shape.Add(new Vector2(0, 1));
                cliffTemplate.Shape.Add(new Vector2(1, 1));
                
                _templates[cliffTemplate.Name] = cliffTemplate;
                _templatesByType["Cliff"].Add(cliffTemplate);
            }
            
            // Initialize border transitions
            for (int i = 0; i < 8; i++)
            {
                _borderTransitions[i] = new Dictionary<int, int>();
                for (int j = 0; j < 8; j++)
                {
                    // Simple transition rule: borders can connect if they're adjacent directions
                    if (Math.Abs(i - j) <= 1 || Math.Abs(i - j) == 7)
                    {
                        _borderTransitions[i][j] = 1;
                    }
                }
            }
        }
        
        /// <summary>
        /// Generate a new random map
        /// </summary>
        public GeneratedMap Generate()
        {
            Console.WriteLine($"Generating map with seed {Seed}, size {Size}x{Size}");
            
            // Create results object
            var result = new GeneratedMap
            {
                Size = Size,
                Tiles = new string[Size * Size],
                Resources = new byte[Size * Size],
                ResourceDensities = new byte[Size * Size],
                Elevation = new float[Size * Size]
            };
            
            // Load templates if not already loaded
            if (_templates.Count == 0)
            {
                LoadTemplates("temperat.yaml");
            }
            
            // 1. Generate base elevation with fractal noise
            Console.WriteLine("Generating elevation noise...");
            var elevation = _noise.GenerateFractalNoiseWithSymmetry(
                Size, 
                Rotations, 
                Mirror, 
                WavelengthScale
            );
            result.Elevation = elevation;
            
            // 2. Calibrate height to achieve desired water coverage
            Console.WriteLine("Calibrating elevation...");
            CalibrateHeight(elevation, 0.0f, Water);
            
            // 3. Fix terrain anomalies to create more natural looking land
            Console.WriteLine("Fixing terrain anomalies...");
            var landPlan = FixTerrain(elevation, Size, TerrainSmoothing, MinimumThickness);
            
            // 4. Generate coastlines
            Console.WriteLine("Tracing and laying coastlines...");
            var coastlines = TracePaths(landPlan, Size);
            coastlines = coastlines.Select(c => TweakPath(c, Size, "Coastline")).ToList();
            
            var coastlinePaths = new List<List<Vector2>>();
            foreach (var coastline in coastlines)
            {
                coastlinePaths.Add(TilePath(result.Tiles, Size, coastline, MinimumThickness));
            }
            
            // 5. Fill remaining terrain
            Console.WriteLine("Filling land and water...");
            var coastlineChirality = CalculatePathChirality(Size, coastlinePaths);
            
            for (int i = 0; i < Size * Size; i++)
            {
                if (!string.IsNullOrEmpty(result.Tiles[i]))
                {
                    continue;
                }
                
                if (coastlineChirality[i] > 0)
                {
                    result.Tiles[i] = "t255"; // Land
                }
                else if (coastlineChirality[i] < 0)
                {
                    result.Tiles[i] = "t1i0"; // Water
                }
                else
                {
                    // No coastlines - use elevation directly
                    result.Tiles[i] = elevation[i] >= 0 ? "t255" : "t1i0";
                }
            }
            
            // 6. Handle forests (simplified for this example)
            if (Forests > 0)
            {
                Console.WriteLine("Adding forests...");
                var forests = _noise.GenerateFractalNoiseWithSymmetry(Size, Rotations, Mirror, WavelengthScale);
                CalibrateHeight(forests, 0.0f, 1.0f - Forests);
                
                for (int i = 0; i < Size * Size; i++)
                {
                    if (forests[i] >= 0 && result.Tiles[i] == "t255")
                    {
                        result.Tiles[i] = "t3"; // Forest tile
                    }
                }
            }
            
            // 7. Generate map YAML
            Console.WriteLine("Generating map YAML...");
            string mapName = !string.IsNullOrEmpty(CustomName) ? CustomName : $"Random Map {Seed}";
            
            result.MapYaml = $@"MapFormat: 12
RequiresMod: ra
Title: {mapName}
Author: OpenRA.MapReader
Tileset: TEMPERAT
MapSize: {Size+2},{Size+2}
Bounds: 1,1,{Size},{Size}
Visibility: Lobby
Categories: Conquest

Players:
	PlayerReference@Neutral:
		Name: Neutral
		OwnsWorld: True
		NonCombatant: True
		Faction: england
	PlayerReference@Creeps:
		Name: Creeps
		NonCombatant: True
		Faction: england";

            // Add players
            for (int i = 0; i < Players; i++)
            {
                result.MapYaml += $@"
	PlayerReference@Multi{i}:
		Name: Multi{i}
		Playable: True
		Faction: Random
		Enemies: Creeps";
            }
            
            // 8. Generate binary map data
            Console.WriteLine("Generating binary map data...");
            result.BinaryMap = GenerateBinaryMap(result);
            
            return result;
        }

        /// <summary>
        /// Calibrate height values to achieve a specific water coverage
        /// </summary>
        private void CalibrateHeight(float[] values, float target, float fraction)
        {
            // Sort values to find the threshold for the desired fraction
            var sorted = new float[values.Length];
            Array.Copy(values, sorted, values.Length);
            Array.Sort(sorted);
            
            // Calculate the adjustment needed
            float adjustment = target - sorted[(int)(sorted.Length * fraction)];
            
            // Apply adjustment to all values
            for (int i = 0; i < values.Length; i++)
            {
                values[i] += adjustment;
            }
        }

        /// <summary>
        /// Fix terrain anomalies to create more natural looking shorelines
        /// </summary>
        private float[] FixTerrain(float[] elevation, int size, int smoothing, int minThickness)
        {
            // Create a discrete height map with values -1 and 1
            var landmass = new float[size * size];
            for (int i = 0; i < size * size; i++)
            {
                landmass[i] = elevation[i] >= 0 ? 1 : -1;
            }

            // Apply median smoothing to create more natural terrain
            for (int pass = 0; pass < 2; pass++)
            {
                for (int radius = 1; radius <= smoothing; radius++)
                {
                    // For simplicity in this example, we'll just check a few neighbors
                    // In a full implementation, this would be a true median filter
                    var smoothed = new float[size * size];
                    
                    for (int y = 0; y < size; y++)
                    {
                        for (int x = 0; x < size; x++)
                        {
                            int land = 0;
                            int water = 0;
                            
                            // Check neighbors in a radius
                            for (int dy = -radius; dy <= radius; dy++)
                            {
                                for (int dx = -radius; dx <= radius; dx++)
                                {
                                    int nx = x + dx;
                                    int ny = y + dy;
                                    
                                    if (nx >= 0 && nx < size && ny >= 0 && ny < size)
                                    {
                                        if (landmass[ny * size + nx] > 0)
                                        {
                                            land++;
                                        }
                                        else
                                        {
                                            water++;
                                        }
                                    }
                                }
                            }
                            
                            // Assign based on majority
                            smoothed[y * size + x] = (land >= water) ? 1 : -1;
                        }
                    }
                    
                    landmass = smoothed;
                }
                
                // Fix thin areas by ensuring minimum thickness
                FixThinAreas(landmass, size, true, minThickness);
                FixThinAreas(landmass, size, false, minThickness);
            }
            
            return landmass;
        }

        /// <summary>
        /// Fix thin areas of terrain to ensure minimum thickness
        /// </summary>
        private void FixThinAreas(float[] terrain, int size, bool fixLand, int minThickness)
        {
            // In a full implementation, this would ensure minimum land/water thickness
            // For this example, we'll do a simplified version
            
            var newTerrain = new float[size * size];
            Array.Copy(terrain, newTerrain, terrain.Length);
            
            for (int y = minThickness; y < size - minThickness; y++)
            {
                for (int x = minThickness; x < size - minThickness; x++)
                {
                    int i = y * size + x;
                    bool isLand = terrain[i] > 0;
                    
                    if (isLand == fixLand)
                    {
                        continue;
                    }
                    
                    // Check if this is a thin area by counting neighbors
                    int oppositeCount = 0;
                    for (int dy = -minThickness; dy <= minThickness; dy++)
                    {
                        for (int dx = -minThickness; dx <= minThickness; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            
                            int nx = x + dx;
                            int ny = y + dy;
                            
                            if (nx >= 0 && nx < size && ny >= 0 && ny < size)
                            {
                                bool neighborIsLand = terrain[ny * size + nx] > 0;
                                if (neighborIsLand == fixLand)
                                {
                                    oppositeCount++;
                                }
                            }
                        }
                    }
                    
                    // If we're surrounded by the opposite terrain type, fill in
                    if (oppositeCount > minThickness * minThickness)
                    {
                        newTerrain[i] = fixLand ? 1 : -1;
                    }
                }
            }
            
            Array.Copy(newTerrain, terrain, terrain.Length);
        }

        /// <summary>
        /// Trace paths along terrain boundaries
        /// </summary>
        private List<TerrainPath> TracePaths(float[] elevation, int size)
        {
            var paths = new List<TerrainPath>();
            
            // Calculate horizontal and vertical gradients to find edges
            var gradientH = new sbyte[size * size];
            var gradientV = new sbyte[size * size];
            
            for (int y = 0; y < size; y++)
            {
                for (int x = 1; x < size; x++)
                {
                    int i = y * size + x;
                    int l = elevation[i - 1] >= 0 ? 1 : 0;
                    int r = elevation[i] >= 0 ? 1 : 0;
                    gradientV[i] = (sbyte)(r - l);
                }
            }
            
            for (int y = 1; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int i = y * size + x;
                    int u = elevation[i - size] >= 0 ? 1 : 0;
                    int d = elevation[i] >= 0 ? 1 : 0;
                    gradientH[i] = (sbyte)(d - u);
                }
            }
            
            // Trace paths along the gradients
            var visited = new bool[size * size];
            
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int i = y * size + x;
                    
                    // Check for horizontal gradient
                    if (gradientH[i] != 0 && !visited[i])
                    {
                        var path = new TerrainPath("Coastline");
                        TracePath(x, y, gradientH, gradientV, size, visited, path);
                        paths.Add(path);
                    }
                    
                    // Check for vertical gradient
                    if (gradientV[i] != 0 && !visited[i])
                    {
                        var path = new TerrainPath("Coastline");
                        TracePath(x, y, gradientH, gradientV, size, visited, path);
                        paths.Add(path);
                    }
                }
            }
            
            return paths;
        }
        
        /// <summary>
        /// Trace a single path along terrain boundaries
        /// </summary>
        private void TracePath(int startX, int startY, sbyte[] gradientH, sbyte[] gradientV, int size, bool[] visited, TerrainPath path)
        {
            int x = startX;
            int y = startY;
            int direction = DIRECTION_R; // Default starting direction
            
            // Add the starting point
            path.Points.Add(new Vector2(x, y));
            visited[y * size + x] = true;
            
            while (true)
            {
                // Determine next direction based on gradients
                int i = y * size + x;
                bool moveR = x < size - 1 && gradientH[i] > 0;
                bool moveD = y < size - 1 && gradientV[i] < 0;
                bool moveL = x > 0 && gradientH[i - 1] < 0;
                bool moveU = y > 0 && gradientV[i - size] > 0;
                
                // Clear current gradient to avoid revisiting
                if (direction == DIRECTION_R || direction == DIRECTION_L)
                {
                    gradientH[i] = 0;
                }
                else if (direction == DIRECTION_U || direction == DIRECTION_D)
                {
                    gradientV[i] = 0;
                }
                
                // Try to continue in same general direction first
                if (direction == DIRECTION_R && moveU)
                {
                    direction = DIRECTION_U;
                }
                else if (direction == DIRECTION_D && moveR)
                {
                    direction = DIRECTION_R;
                }
                else if (direction == DIRECTION_L && moveD)
                {
                    direction = DIRECTION_D;
                }
                else if (direction == DIRECTION_U && moveL)
                {
                    direction = DIRECTION_L;
                }
                else if (moveR)
                {
                    direction = DIRECTION_R;
                }
                else if (moveD)
                {
                    direction = DIRECTION_D;
                }
                else if (moveL)
                {
                    direction = DIRECTION_L;
                }
                else if (moveU)
                {
                    direction = DIRECTION_U;
                }
                else
                {
                    // Dead end - path complete
                    break;
                }
                
                // Move to next position
                switch (direction)
                {
                    case DIRECTION_R:
                        x++;
                        break;
                    case DIRECTION_D:
                        y++;
                        break;
                    case DIRECTION_L:
                        x--;
                        break;
                    case DIRECTION_U:
                        y--;
                        break;
                }
                
                // Add the new point
                path.Points.Add(new Vector2(x, y));
                visited[y * size + x] = true;
                
                // Check if we've completed a loop
                if (x == startX && y == startY)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Prepare a path for template placement
        /// </summary>
        private TerrainPath TweakPath(TerrainPath path, int size, string type)
        {
            var tweaked = new TerrainPath(type)
            {
                Points = new List<Vector2>(path.Points)
            };
            
            // Check if this is a loop (start and end points match)
            var points = tweaked.Points;
            tweaked.IsLoop = (points.Count > 1 && 
                              points[0].X == points[points.Count - 1].X && 
                              points[0].Y == points[points.Count - 1].Y);
            
            // Set start and end types
            tweaked.StartType = type;
            tweaked.EndType = type;
            
            // Calculate start and end directions
            if (points.Count >= 2)
            {
                tweaked.StartDirN = CalculateDirection(points[0], points[1]);
                
                if (tweaked.IsLoop)
                {
                    tweaked.EndDirN = tweaked.StartDirN;
                }
                else
                {
                    tweaked.EndDirN = CalculateDirection(
                        points[points.Count - 2], 
                        points[points.Count - 1]
                    );
                }
            }
            
            return tweaked;
        }

        /// <summary>
        /// Calculate direction between two points
        /// </summary>
        private int CalculateDirection(Vector2 from, Vector2 to)
        {
            float dx = to.X - from.X;
            float dy = to.Y - from.Y;
            
            // Convert to 8-direction
            if (dx > 0)
            {
                if (dy > 0) return DIRECTION_RD;
                if (dy < 0) return DIRECTION_RU;
                return DIRECTION_R;
            }
            if (dx < 0)
            {
                if (dy > 0) return DIRECTION_LD;
                if (dy < 0) return DIRECTION_LU;
                return DIRECTION_L;
            }
            if (dy > 0) return DIRECTION_D;
            if (dy < 0) return DIRECTION_U;
            
            // Should not happen
            return DIRECTION_R;
        }

        /// <summary>
        /// Place templates along a path
        /// </summary>
        private List<Vector2> TilePath(string[] tiles, int size, TerrainPath path, int minThickness)
        {
            // Get available templates for this path type
            var availableTemplates = _templatesByType.ContainsKey(path.Type) 
                ? _templatesByType[path.Type] 
                : new List<TerrainTemplate>();
            
            if (availableTemplates.Count == 0)
            {
                Console.WriteLine($"Warning: No templates available for path type {path.Type}");
                return path.Points.ToList();
            }
            
            // In a full implementation, this would use the complex template selection algorithm
            // For this example, we'll use a simplified approach
            var points = path.Points;
            var placedPoints = new List<Vector2>();
            
            for (int i = 1; i < points.Count; i++)
            {
                // Get current position and direction
                int x = (int)points[i - 1].X;
                int y = (int)points[i - 1].Y;
                int nextX = (int)points[i].X;
                int nextY = (int)points[i].Y;
                int dir = CalculateDirection(points[i - 1], points[i]);
                
                // Find a template that fits this direction
                var template = availableTemplates.FirstOrDefault(t => t.StartBorderN == dir);
                if (template == null)
                {
                    template = availableTemplates.First(); // Fallback
                }
                
                // Place the template
                for (int j = 0; j < template.Shape.Count; j++)
                {
                    int tx = x + (int)template.Shape[j].X;
                    int ty = y + (int)template.Shape[j].Y;
                    
                    if (tx >= 0 && tx < size && ty >= 0 && ty < size)
                    {
                        tiles[ty * size + tx] = $"t{template.Id}i{j}";
                        placedPoints.Add(new Vector2(tx, ty));
                    }
                }
            }
            
            return placedPoints;
        }

        /// <summary>
        /// Calculate path chirality (clockwise vs counterclockwise direction)
        /// </summary>
        private sbyte[] CalculatePathChirality(int size, List<List<Vector2>> paths)
        {
            var chirality = new sbyte[size * size];
            
            // For each path segment, determine its contribution to chirality
            foreach (var path in paths)
            {
                for (int i = 1; i < path.Count; i++)
                {
                    var from = path[i - 1];
                    var to = path[i];
                    int fx = (int)from.X;
                    int fy = (int)from.Y;
                    int dir = CalculateDirection(from, to);
                    
                    switch (dir)
                    {
                        case DIRECTION_R:
                            if (fy < size) chirality[fy * size + fx]++;
                            if (fy > 0) chirality[(fy - 1) * size + fx]--;
                            break;
                        case DIRECTION_D:
                            if (fx > 0) chirality[fy * size + (fx - 1)]++;
                            if (fx < size) chirality[fy * size + fx]--;
                            break;
                        case DIRECTION_L:
                            if (fy > 0) chirality[(fy - 1) * size + (fx - 1)]++;
                            if (fy < size) chirality[fy * size + (fx - 1)]--;
                            break;
                        case DIRECTION_U:
                            if (fx < size) chirality[(fy - 1) * size + fx]++;
                            if (fx > 0) chirality[(fy - 1) * size + (fx - 1)]--;
                            break;
                    }
                }
            }
            
            // Spread chirality values to fill regions
            var filledChirality = new sbyte[size * size];
            Array.Copy(chirality, filledChirality, chirality.Length);
            
            bool changed;
            do
            {
                changed = false;
                
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        int i = y * size + x;
                        if (filledChirality[i] != 0)
                        {
                            continue;
                        }
                        
                        // Check neighbors for non-zero chirality
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                if (dx == 0 && dy == 0)
                                {
                                    continue;
                                }
                                
                                int nx = x + dx;
                                int ny = y + dy;
                                
                                if (nx >= 0 && nx < size && ny >= 0 && ny < size)
                                {
                                    int ni = ny * size + nx;
                                    if (filledChirality[ni] != 0)
                                    {
                                        filledChirality[i] = filledChirality[ni];
                                        changed = true;
                                        break;
                                    }
                                }
                            }
                            
                            if (filledChirality[i] != 0)
                            {
                                break;
                            }
                        }
                    }
                }
            } while (changed);
            
            return filledChirality;
        }

        /// <summary>
        /// Generate the binary map data
        /// </summary>
        private byte[] GenerateBinaryMap(GeneratedMap map)
        {
            // Size of the map including 1-cell border
            int binSize = (map.Size + 2) * (map.Size + 2);
            
            // Header + tiles + resources
            int dataSize = 17 + 3 * binSize + 2 * binSize;
            var data = new byte[dataSize];
            
            // Write header
            data[0] = 2; // Format version
            WriteU16(data, 1, (ushort)(map.Size + 2)); // Width
            WriteU16(data, 3, (ushort)(map.Size + 2)); // Height
            WriteU32(data, 5, 17); // Tile offset
            WriteU32(data, 9, 0);  // Height map offset (not used)
            WriteU32(data, 13, (uint)(17 + 3 * binSize)); // Resources offset - Cast to uint to fix compilation error
            
            // Initialize with default grass
            for (int i = 0; i < binSize; i++)
            {
                WriteU16(data, 17 + i * 3, 255); // Template ID (255 = grass)
                data[17 + i * 3 + 2] = 0; // Index
                data[17 + 3 * binSize + i * 2] = 0; // Resource type
                data[17 + 3 * binSize + i * 2 + 1] = 0; // Resource density
            }
            
            // Write actual map data (shifted by 1 to account for border)
            for (int y = 0; y < map.Size; y++)
            {
                for (int x = 0; x < map.Size; x++)
                {
                    // Get tile data
                    string tileCode = map.Tiles[y * map.Size + x];
                    if (string.IsNullOrEmpty(tileCode))
                    {
                        continue; // Use default
                    }
                    
                    // Parse template and index
                    int templateId = 255; // Default grass
                    int tileIndex = 0;
                    
                    if (tileCode.StartsWith("t") && tileCode.Contains("i"))
                    {
                        var parts = tileCode.Substring(1).Split('i');
                        if (parts.Length == 2 && 
                            int.TryParse(parts[0], out int tid) && 
                            int.TryParse(parts[1], out int idx))
                        {
                            templateId = tid;
                            tileIndex = idx;
                        }
                    }
                    
                    // Calculate offset in binary data (+1 for border)
                    int binIndex = (y + 1) * (map.Size + 2) + (x + 1);
                    
                    // Write tile data
                    WriteU16(data, 17 + binIndex * 3, (ushort)templateId);
                    data[17 + binIndex * 3 + 2] = (byte)tileIndex;
                    
                    // Write resource data
                    data[17 + 3 * binSize + binIndex * 2] = map.Resources[y * map.Size + x];
                    data[17 + 3 * binSize + binIndex * 2 + 1] = map.ResourceDensities[y * map.Size + x];
                }
            }
            
            return data;
        }
        
        /// <summary>
        /// Write a 16-bit value to a byte array
        /// </summary>
        private void WriteU16(byte[] buffer, int offset, ushort value)
        {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
        }
        
        /// <summary>
        /// Write a 32-bit value to a byte array
        /// </summary>
        private void WriteU32(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
            buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
            buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
        }
    }

    /// <summary>
    /// Contains the generated map data
    /// </summary>
    public class GeneratedMap
    {
        public int Size { get; set; }
        public string[] Tiles { get; set; }
        public byte[] Resources { get; set; }
        public byte[] ResourceDensities { get; set; }
        public float[] Elevation { get; set; }
        public string MapYaml { get; set; }
        public byte[] BinaryMap { get; set; }
        
        /// <summary>
        /// Save the map files to disk
        /// </summary>
        public void SaveToFiles(string basePath)
        {
            // Create the directory if it doesn't exist
            Directory.CreateDirectory(basePath);
            
            // Save the map.yaml file
            File.WriteAllText(Path.Combine(basePath, "map.yaml"), MapYaml);
            
            // Save the map.bin file
            File.WriteAllBytes(Path.Combine(basePath, "map.bin"), BinaryMap);
            
            // Generate and save a preview image
            using (var image = new Image<Rgba32>(Size, Size))
            {
                // Fill with terrain colors
                for (int y = 0; y < Size; y++)
                {
                    for (int x = 0; x < Size; x++)
                    {
                        int i = y * Size + x;
                        string tileCode = Tiles[i];
                        
                        // Set color based on tile type
                        // Fix: Use SixLabors.ImageSharp Color instead of a Color with R,G,B,A properties
                        Rgba32 pixelColor;
                        if (tileCode.StartsWith("t1"))
                            pixelColor = new Rgba32(0, 0, 255); // Water (Blue)
                        else if (tileCode.StartsWith("t3"))
                            pixelColor = new Rgba32(0, 119, 0); // Forest (ForestGreen)
                        else
                            pixelColor = new Rgba32(0, 255, 0); // Land (Green)
                        
                        // Show resources
                        if (Resources[i] > 0)
                        {
                            pixelColor = Resources[i] == 1 
                                ? new Rgba32(255, 215, 0)   // Gold
                                : new Rgba32(128, 0, 128);  // Purple
                        }
                        
                        image[x, y] = pixelColor;
                    }
                }
                
                // Save the preview
                image.Save(Path.Combine(basePath, "map_preview.png"));
            }
        }
    }
}
