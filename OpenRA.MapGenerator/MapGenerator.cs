using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace OpenRA.MapGenerator
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
        public string TileSet { get; set; } = "TEMPERAT";
        public string MapType { get; set; } = "ra";
        public string[] Factions { get; set; } = new[] { "england", "germany", "france", "ukraine", "russia" };
        public string[] Colors { get; set; } = new[] { "E4302E", "0A9FC3", "CAA700", "1C9331", "7F0A83", "F8B700", "0F33C6", "C9C9C9" };

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
                    // Only connect matching borders or adjacent ones
                    if (i == j || Math.Abs(i - j) == 1 || Math.Abs(i - j) == 7)
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
                Elevation = new float[Size * Size],
                Entities = new List<Entity>()
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
            
            // 7. Generate player entities and resources
            Console.WriteLine("Generating player entities and resources...");
            var players = new List<Entity>();
            
            // Add player starting locations with proper symmetry
            for (int i = 0; i < Players; i++)
            {
                // Calculate symmetric positions around the center
                float angle = (float)(i * 2 * Math.PI / Players);
                if (Rotations > 0)
                {
                    // Align to the rotational symmetry
                    angle = (float)(i * 2 * Math.PI / Rotations);
                }
                
                float radius = Size / 3.0f;
                float centerX = Size / 2.0f;
                float centerY = Size / 2.0f;
                
                int x = (int)(centerX + radius * Math.Cos(angle));
                int y = (int)(centerY + radius * Math.Sin(angle));
                
                // Make sure the position is on land
                int maxAttempts = 10;
                for (int attempt = 0; attempt < maxAttempts; attempt++)
                {
                    int idx = y * Size + x;
                    if (idx >= 0 && idx < Size * Size && result.Tiles[idx] == "t255")
                    {
                        break; // Found a good spot
                    }
                    
                    // Try a slightly different position
                    x = (int)(centerX + (radius + attempt * 5) * Math.Cos(angle));
                    y = (int)(centerY + (radius + attempt * 5) * Math.Sin(angle));
                    
                    // Keep in bounds
                    x = Math.Clamp(x, 5, Size - 6);
                    y = Math.Clamp(y, 5, Size - 6);
                }
                
                // Add player entity
                players.Add(new Entity
                {
                    Type = "mpspawn",
                    Owner = $"Multi{i}",
                    X = x,
                    Y = y
                });
                
                // Add resources around player start
                AddResourcesAroundPoint(result.Resources, result.ResourceDensities, Size, x, y, 5, 3, 1);
            }
            
            // 8. Add tech structures and resource fields
            Console.WriteLine("Adding tech structures and resources...");
            var entities = new List<Entity>();
            
            // Add neutral tech structures
            AddNeutralStructures(entities, Size, 3);
            
            // Add bonus resource fields
            for (int i = 0; i < 5; i++)
            {
                int x = _random.Next(Size / 4, 3 * Size / 4);
                int y = _random.Next(Size / 4, 3 * Size / 4);
                
                // Don't place too close to players
                bool tooClose = false;
                foreach (var player in players)
                {
                    float dist = (player.X - x) * (player.X - x) + (player.Y - y) * (player.Y - y);
                    if (dist < 20 * 20)
                    {
                        tooClose = true;
                        break;
                    }
                }
                
                if (!tooClose)
                {
                    AddResourcesAroundPoint(result.Resources, result.ResourceDensities, Size, x, y, 8, 5, 1);
                    
                    // Add a gem patch occasionally
                    if (_random.NextDouble() < 0.3)
                    {
                        AddResourcesAroundPoint(result.Resources, result.ResourceDensities, Size, 
                            x + _random.Next(-5, 6), 
                            y + _random.Next(-5, 6), 
                            3, 2, 2);
                    }
                }
            }
            
            // Add all entities to the result
            result.Entities.AddRange(players);
            result.Entities.AddRange(entities);
            
            // Generate the binary map data
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
            // Find bounding box for this path
            float minPointX = float.MaxValue;
            float minPointY = float.MaxValue;
            float maxPointX = float.MinValue;
            float maxPointY = float.MinValue;
            
            foreach (var point in path.Points)
            {
                minPointX = Math.Min(minPointX, point.X);
                minPointY = Math.Min(minPointY, point.Y);
                maxPointX = Math.Max(maxPointX, point.X);
                maxPointY = Math.Max(maxPointY, point.Y);
            }
            
            // Add margin for minimum thickness
            int maxDeviation = (minThickness - 1) / 2;
            minPointX -= maxDeviation;
            minPointY -= maxDeviation;
            maxPointX += maxDeviation;
            maxPointY += maxDeviation;
            
            // Shift all points to local coordinates
            var points = path.Points.Select(p => new Vector2(
                p.X - minPointX,
                p.Y - minPointY
            )).ToList();
            
            // Calculate local grid size for the path
            int sizeX = (int)(maxPointX - minPointX) + 1;
            int sizeY = (int)(maxPointY - minPointY) + 1;
            int sizeXY = sizeX * sizeY;
            
            // Create data structures for path scoring
            var deviations = new uint[sizeXY];
            var traversables = new byte[sizeXY];
            var directions = new byte[sizeXY];
            
            for (int i = 0; i < sizeXY; i++)
            {
                deviations[i] = uint.MaxValue;
            }
            
            // Calculate deviations, traversable directions, and direction masks
            for (int pointI = 0; pointI < points.Count; pointI++)
            {
                if (path.IsLoop && pointI == 0)
                {
                    // Skip first point for loops as it's the same as the last
                    continue;
                }
                
                var point = points[pointI];
                int pointPrevI = pointI - 1;
                int pointNextI = pointI + 1;
                float directionX = 0;
                float directionY = 0;
                
                if (pointNextI < points.Count)
                {
                    directionX += points[pointNextI].X - point.X;
                    directionY += points[pointNextI].Y - point.Y;
                }
                
                if (pointPrevI >= 0)
                {
                    directionX += point.X - points[pointPrevI].X;
                    directionY += point.Y - points[pointPrevI].Y;
                }
                
                // Calculate deviation for this point and surrounding area
                for (int deviation = 0; deviation <= maxDeviation; deviation++)
                {
                    int minX = (int)Math.Floor(point.X - deviation);
                    int minY = (int)Math.Floor(point.Y - deviation);
                    int maxX = (int)Math.Ceiling(point.X + deviation);
                    int maxY = (int)Math.Ceiling(point.Y + deviation);
                    
                    for (int y = minY; y <= maxY; y++)
                    {
                        for (int x = minX; x <= maxX; x++)
                        {
                            if (x < 0 || x >= sizeX || y < 0 || y >= sizeY)
                            {
                                continue;
                            }
                            
                            int i = y * sizeX + x;
                            
                            if (deviation < deviations[i])
                            {
                                deviations[i] = (uint)deviation;
                            }
                            
                            if (deviation == maxDeviation)
                            {
                                // Calculate traversable directions for templates
                                if (x > minX) traversables[i] |= (1 << DIRECTION_L);
                                if (x < maxX) traversables[i] |= (1 << DIRECTION_R);
                                if (y > minY) traversables[i] |= (1 << DIRECTION_U);
                                if (y < maxY) traversables[i] |= (1 << DIRECTION_D);
                                if (x > minX && y > minY) traversables[i] |= (1 << DIRECTION_LU);
                                if (x > minX && y < maxY) traversables[i] |= (1 << DIRECTION_LD);
                                if (x < maxX && y > minY) traversables[i] |= (1 << DIRECTION_RU);
                                if (x < maxX && y < maxY) traversables[i] |= (1 << DIRECTION_RD);
                            }
                        }
                    }
                }
            }
            
            // Calculate direction masks for each grid point
            for (int i = 0; i < sizeXY; i++)
            {
                if (deviations[i] == uint.MaxValue)
                {
                    continue;
                }
                
                int x = i % sizeX;
                int y = i / sizeX;
                
                // Determine main direction of the path at this point
                float dx = 0, dy = 0;
                
                for (int j = 1; j < points.Count; j++)
                {
                    float dist = (points[j].X - x) * (points[j].X - x) + (points[j].Y - y) * (points[j].Y - y);
                    if (dist < 5 * 5) // Within close range
                    {
                        // Contribute to direction based on path segment
                        float segmentDx = points[j].X - points[j - 1].X;
                        float segmentDy = points[j].Y - points[j - 1].Y;
                        float weight = 1.0f / (dist + 1);
                        
                        dx += segmentDx * weight;
                        dy += segmentDy * weight;
                    }
                }
                
                if (dx != 0 || dy != 0)
                {
                    // Calculate direction based on vector
                    int direction = CalculateDirectionXY(dx, dy);
                    
                    // Set permitted directions (allow 3 adjacent directions)
                    directions[i] = (byte)(1 << direction);
                    directions[i] |= (byte)(1 << ((direction + 1) % 8));
                    directions[i] |= (byte)(1 << ((direction + 7) % 8));
                }
            }
            
            // Get relevant templates and organize by border
            var availableTemplates = _templatesByType.ContainsKey(path.Type) 
                ? _templatesByType[path.Type] 
                : new List<TerrainTemplate>();
                
            if (availableTemplates.Count == 0)
            {
                Console.WriteLine($"Warning: No templates available for path type {path.Type}");
                return path.Points.ToList();
            }
            
            // Organize templates by border
            var templatesByStartBorder = new Dictionary<int, List<TerrainTemplate>>();
            var templatesByEndBorder = new Dictionary<int, List<TerrainTemplate>>();
            
            foreach (var template in availableTemplates)
            {
                if (!templatesByStartBorder.ContainsKey(template.StartBorderN))
                {
                    templatesByStartBorder[template.StartBorderN] = new List<TerrainTemplate>();
                }
                
                if (!templatesByEndBorder.ContainsKey(template.EndBorderN))
                {
                    templatesByEndBorder[template.EndBorderN] = new List<TerrainTemplate>();
                }
                
                templatesByStartBorder[template.StartBorderN].Add(template);
                templatesByEndBorder[template.EndBorderN].Add(template);
            }
            
            // Place tiles along the path
            var resultPath = new List<Vector2>();
            
            // Simple template placement algorithm - for each point, find and place the best template
            for (int i = 1; i < points.Count; i++)
            {
                int startX = (int)points[i - 1].X;
                int startY = (int)points[i - 1].Y;
                int endX = (int)points[i].X;
                int endY = (int)points[i].Y;
                
                // Calculate direction of this segment
                int direction = CalculateDirection(
                    new Vector2(startX, startY),
                    new Vector2(endX, endY)
                );
                
                // Find templates that match this direction
                var matchingTemplates = templatesByStartBorder.ContainsKey(direction)
                    ? templatesByStartBorder[direction]
                    : new List<TerrainTemplate>();
                
                if (matchingTemplates.Count == 0)
                {
                    // If no exact match, find the closest direction
                    int bestDiff = 8;
                    int bestDir = direction;
                    
                    foreach (var dir in templatesByStartBorder.Keys)
                    {
                        int diff = Math.Min(Math.Abs(dir - direction), Math.Min(dir + 8 - direction, direction + 8 - dir));
                        if (diff < bestDiff)
                        {
                            bestDiff = diff;
                            bestDir = dir;
                        }
                    }
                    
                    matchingTemplates = templatesByStartBorder[bestDir];
                }
                
                // Pick a random matching template
                int templateIndex = _random.Next(matchingTemplates.Count);
                var template = matchingTemplates[templateIndex];
                
                // Place the template at the start position
                int globalX = (int)(startX + minPointX);
                int globalY = (int)(startY + minPointY);
                
                // Place all tiles in the template
                for (int j = 0; j < template.Shape.Count; j++)
                {
                    int tx = globalX + (int)template.Shape[j].X;
                    int ty = globalY + (int)template.Shape[j].Y;
                    
                    if (tx >= 0 && tx < size && ty >= 0 && ty < size)
                    {
                        tiles[ty * size + tx] = $"t{template.Id}i{j}";
                        resultPath.Add(new Vector2(tx, ty));
                    }
                }
            }
            
            return resultPath;
        }
        
        /// <summary>
        /// Calculate direction from a vector
        /// </summary>
        private int CalculateDirectionXY(float dx, float dy)
        {
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
            
            // Default if both are zero
            return DIRECTION_R;
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
        /// Write the map YAML file
        /// </summary>
        public void WriteYaml(GeneratedMap map, string yamlPath)
        {
            Console.WriteLine("Writing map.yaml file...");
            using (var writer = new StreamWriter(yamlPath))
            {
                writer.WriteLine("MapFormat: 11");
                writer.WriteLine();
                writer.WriteLine("RequiresMod: ra");
                writer.WriteLine();
                writer.WriteLine("Title: Randomly Generated Map");
                writer.WriteLine("Author: OpenRA Map Generator");
                writer.WriteLine("Tileset: TEMPERAT");
                writer.WriteLine("MapSize: {0},{0}", map.Size + 2);
                writer.WriteLine("Bounds: 1,1,{0},{0}", map.Size);
                writer.WriteLine();
                writer.WriteLine("Visibility: MissionSelector, Lobby");
                writer.WriteLine("Categories: System");
                writer.WriteLine("LockPreview: True");
                writer.WriteLine("HideTileSpriteLayer: True");
                writer.WriteLine();
                writer.WriteLine("Players:");
                writer.WriteLine("  PlayerReference@Neutral:");
                writer.WriteLine("    Name: Neutral");
                writer.WriteLine("    OwnsWorld: True");
                writer.WriteLine("    NonCombatant: True");
                writer.WriteLine("    Faction: england");
                writer.WriteLine("  PlayerReference@Creeps:");
                writer.WriteLine("    Name: Creeps");
                writer.WriteLine("    NonCombatant: True");
                writer.WriteLine("    Faction: england");

                for (int i = 0; i < Players; i++)
                {
                    writer.WriteLine("  PlayerReference@Multi{0}:", i);
                    writer.WriteLine("    Name: Multi{0}", i);
                    writer.WriteLine("    Playable: True");
                    writer.WriteLine("    AllowBots: True");
                    writer.WriteLine("    LockFaction: False");
                    writer.WriteLine("    LockColor: True");
                    writer.WriteLine("    LockSpawn: False");
                    writer.WriteLine("    LockTeam: False");
                    writer.WriteLine("    DefaultStartingUnits: True");
                    writer.WriteLine("    Faction: Random");
                }
                writer.WriteLine();
                writer.WriteLine("Rules:");
                writer.WriteLine("  Player:");
                for (int i = 0; i < Players; i++)
                {
                    writer.WriteLine("    multi{0}:", i);
                    writer.WriteLine("      Faction: {0}", Factions[i % Factions.Length]);
                    writer.WriteLine("      Color: {0}", Colors[i % Colors.Length]);
                }
                
                // Write Actors section for entities
                if (map.Entities.Count > 0)
                {
                    writer.WriteLine();
                    writer.WriteLine("Actors:");
                    int actorId = 0;
                    
                    foreach (var entity in map.Entities)
                    {
                        writer.WriteLine("  Actor{0}:", actorId);
                        writer.WriteLine("    Location: {0},{1}", entity.X + 1, entity.Y + 1);
                        writer.WriteLine("    Owner: {0}", entity.Owner);
                        writer.WriteLine("    Type: {0}", entity.Type);
                        
                        actorId++;
                    }
                }
                
                // Write Resources section for resource entities            if (map.Resources.Any(r => r != 0))
            {
                writer.WriteLine();
                writer.WriteLine("ResourceLayer:");
                
                for (int y = 0; y < map.Size; y++)
                {
                    for (int x = 0; x < map.Size; x++)
                    {
                        int idx = y * map.Size + x;
                        if (idx < map.Resources.Length && map.Resources[idx] != 0)
                        {
                            writer.WriteLine("  {0},{1}: {2},{3}", 
                                x + 1, y + 1, 
                                map.Resources[idx], 
                                map.ResourceDensities.Length > idx ? map.ResourceDensities[idx] : 255);
                        }
                    }
                }
            }
            }
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
        
        /// <summary>
        /// Adds resources around a point to create ore fields
        /// </summary>
        private void AddResourcesAroundPoint(byte[] resources, byte[] densities, int size, int centerX, int centerY, int radius, int amount, byte resourceType)
        {
            for (int i = 0; i < amount; i++)
            {
                // Random angle and distance within radius
                double angle = _random.NextDouble() * Math.PI * 2;
                double distance = _random.NextDouble() * radius;
                
                int x = (int)(centerX + Math.Cos(angle) * distance);
                int y = (int)(centerY + Math.Sin(angle) * distance);
                
                // Make sure coordinates are in bounds
                if (x >= 0 && x < size && y >= 0 && y < size)
                {
                    // Add a small ore patch
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx;
                            int ny = y + dy;
                            
                            if (nx >= 0 && nx < size && ny >= 0 && ny < size)
                            {
                                resources[ny * size + nx] = resourceType;
                                densities[ny * size + nx] = (byte)(_random.Next(40, 100));
                            }
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// Adds neutral structures to the map
        /// </summary>
        private void AddNeutralStructures(List<Entity> entities, int size, int count)
        {
            string[] structureTypes = { "oilb", "hosp", "miss", "bio" };
            float centerX = size / 2.0f;
            float centerY = size / 2.0f;
            
            for (int i = 0; i < count; i++)
            {
                // Choose a random structure type
                string type = structureTypes[_random.Next(structureTypes.Length)];
                
                // Place at random position away from center
                double angle = _random.NextDouble() * Math.PI * 2;
                double distance = size * 0.25 + _random.NextDouble() * size * 0.15;
                
                int x = (int)(centerX + Math.Cos(angle) * distance);
                int y = (int)(centerY + Math.Sin(angle) * distance);
                
                // Make sure coordinates are valid
                x = Math.Clamp(x, 2, size - 3);
                y = Math.Clamp(y, 2, size - 3);
                
                // Add the entity
                entities.Add(new Entity
                {
                    Type = type,
                    Owner = "Neutral",
                    X = x,
                    Y = y
                });
            }
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
        public byte[] BinaryMap { get; set; }
        public List<Entity> Entities { get; set; } = new();
        
        /// <summary>
        /// Save the map files to disk
        /// </summary>
        public void SaveToFiles(string basePath, MapGenerator generator)
        {
            // Create the directory if it doesn't exist
            Directory.CreateDirectory(basePath);
            
            // Save the map.yaml file
            generator.WriteYaml(this, Path.Combine(basePath, "map.yaml"));
            
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
                
                // Draw entities
                foreach (var entity in Entities)
                {
                    if (entity.X >= 0 && entity.X < Size && entity.Y >= 0 && entity.Y < Size)
                    {
                        // Mark entities with white pixels
                        image[entity.X, entity.Y] = new Rgba32(255, 255, 255);
                        
                        // Mark player entities with different colors
                        if (entity.Type == "mpspawn")
                        {
                            // Draw a small cross to mark player positions
                            for (int dy = -1; dy <= 1; dy++)
                            {
                                for (int dx = -1; dx <= 1; dx++)
                                {
                                    int nx = entity.X + dx;
                                    int ny = entity.Y + dy;
                                    if (nx >= 0 && nx < Size && ny >= 0 && ny < Size)
                                    {
                                        if (dx == 0 || dy == 0)
                                        {
                                            image[nx, ny] = new Rgba32(255, 0, 0);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                
                // Save the preview
                image.Save(Path.Combine(basePath, "map_preview.png"));
            }
        }
    }
}
