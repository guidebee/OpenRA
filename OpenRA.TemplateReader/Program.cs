using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Drawing.Imaging;

namespace OpenRA.TemplateReader
{
    class Program
    {
        static int Main(string[] args)
        {
            Console.WriteLine("OpenRA Template Reader - Export terrain templates as PNG images");

            // Parse command line arguments
            var arguments = new Dictionary<string, string>();
            foreach (var arg in args)
            {
                if (arg.StartsWith("--"))
                {
                    var parts = arg.Substring(2).Split('=', 2);
                    if (parts.Length == 2)
                        arguments[parts[0].Trim()] = parts[1].Trim();
                    else
                        arguments[parts[0].Trim()] = "true";
                }
            }

            // Get required arguments
            string tileset = null;
            ushort? templateId = null;
            string mod = null;
            string gamePath = null;
            string output = Path.Combine(Environment.CurrentDirectory, "template-export");

            if (arguments.TryGetValue("tileset", out var tilesetArg))
                tileset = tilesetArg;

            if (arguments.TryGetValue("template-id", out var templateIdArg) && ushort.TryParse(templateIdArg, out var parsedId))
                templateId = parsedId;

            if (arguments.TryGetValue("mod", out var modArg))
                mod = modArg;

            if (arguments.TryGetValue("game-path", out var gamePathArg))
                gamePath = gamePathArg;

            if (arguments.TryGetValue("output", out var outputArg))
                output = outputArg;

            // Validate required arguments
            if (string.IsNullOrEmpty(tileset))
            {
                Console.WriteLine("Error: --tileset is required");
                PrintUsage();
                return 1;
            }

            if (string.IsNullOrEmpty(mod))
            {
                Console.WriteLine("Error: --mod is required");
                PrintUsage();
                return 1;
            }

            if (string.IsNullOrEmpty(gamePath))
            {
                Console.WriteLine("Error: --game-path is required");
                PrintUsage();
                return 1;
            }

            try
            {
                return ExportTemplates(tileset, templateId, mod, gamePath, output);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return 1;
            }
        }

        static void PrintUsage()
        {
            Console.WriteLine("Usage: templatereader --tileset=<tileset> --mod=<mod> --game-path=<path> [--template-id=<id>] [--output=<path>]");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  templatereader --tileset=desert --template-id=401 --mod=ra --game-path=.");
            Console.WriteLine("  templatereader --tileset=temperat --mod=ra --game-path=. --output=c:/temp/templates");
        }

        static int ExportTemplates(string tileset, ushort? templateId, string mod, string gamePath, string output)
        {
            try
            {
                // Create output directory if it doesn't exist
                var tilesetOutputDir = Path.Combine(output, tileset);
                Directory.CreateDirectory(tilesetOutputDir);

                // Since integrating with OpenRA engine is challenging in this context,
                // we'll create a simplified representation with basic colors

                // First, try to read the tileset definition from yaml
                var tilesetPath = Path.Combine(gamePath, "mods", mod, "tilesets", $"{tileset.ToLowerInvariant()}.yaml");
                if (!File.Exists(tilesetPath))
                {
                    Console.WriteLine($"Error: Tileset file not found at {tilesetPath}");
                    return 1;
                }

                Console.WriteLine($"Reading tileset: {tilesetPath}");
                var tilesetContent = File.ReadAllText(tilesetPath);

                // Parse the templates section
                var templateContent = GetTemplatesSection(tilesetContent);
                if (string.IsNullOrEmpty(templateContent))
                {
                    Console.WriteLine("Error: Could not find Templates section in tileset file");
                    return 1;
                }

                var templates = ParseTemplates(templateContent);
                if (templates.Count == 0)
                {
                    Console.WriteLine("Error: No templates found in tileset file");
                    return 1;
                }

                // Export templates
                if (templateId.HasValue)
                {
                    // Export a single template
                    if (templates.TryGetValue(templateId.Value, out var template))
                    {
                        Console.WriteLine($"Exporting template {templateId.Value}");
                        ExportTemplateInfo(templateId.Value, template, tilesetOutputDir);
                        ExportTemplateImageSimple(templateId.Value, template, tilesetOutputDir);
                    }
                    else
                    {
                        Console.WriteLine($"Error: Template with ID {templateId} not found");
                        return 1;
                    }
                }
                else
                {
                    // Export all templates
                    Console.WriteLine($"Exporting all templates from tileset '{tileset}'");
                    foreach (var template in templates)
                    {
                        Console.WriteLine($"Exporting template {template.Key}");
                        ExportTemplateInfo(template.Key, template.Value, tilesetOutputDir);
                        ExportTemplateImageSimple(template.Key, template.Value, tilesetOutputDir);
                    }
                }

                Console.WriteLine($"Templates exported to {tilesetOutputDir}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return 1;
            }
        }

        static string GetTemplatesSection(string tilesetContent)
        {
            // Simplified YAML parsing to extract the Templates section
            var templateIndex = tilesetContent.IndexOf("Templates:");
            if (templateIndex == -1)
                return null;

            var startIndex = tilesetContent.IndexOf('\n', templateIndex) + 1;
            var endIndex = tilesetContent.Length;

            // Find the next top-level section if it exists
            for (int i = startIndex; i < tilesetContent.Length; i++)
            {
                if (i == 0 || tilesetContent[i - 1] == '\n')
                {
                    if (i < tilesetContent.Length - 1 && !char.IsWhiteSpace(tilesetContent[i]) && tilesetContent[i] != '#')
                    {
                        endIndex = i - 1;
                        break;
                    }
                }
            }

            return tilesetContent.Substring(startIndex, endIndex - startIndex);
        }

        static Dictionary<ushort, TemplateInfo> ParseTemplates(string templatesSection)
        {
            var templates = new Dictionary<ushort, TemplateInfo>();
            var lines = templatesSection.Split('\n');

            TemplateInfo currentTemplate = null;
            ushort currentId = 0;

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith("#"))
                    continue;

                if (trimmedLine.StartsWith("Template@") || trimmedLine.EndsWith(":"))
                {
                    // Parse template ID
                    var idStr = trimmedLine.Split('@', ':')[1].Trim();
                    if (ushort.TryParse(idStr, out var id))
                    {
                        currentId = id;
                        currentTemplate = new TemplateInfo();
                        templates[id] = currentTemplate;
                    }
                }
                else if (currentTemplate != null)
                {
                    // Parse template properties
                    var parts = trimmedLine.Split(':');
                    if (parts.Length >= 2)
                    {
                        var key = parts[0].Trim();
                        var value = string.Join(":", parts.Skip(1)).Trim();

                        switch (key)
                        {
                            case "Id":
                                // Already parsed from the template name
                                break;
                            case "Images":
                                currentTemplate.Images = value.Split(',').Select(img => img.Trim()).ToList();
                                break;
                            case "Size":
                                var sizes = value.Split(',');
                                if (sizes.Length == 2 && int.TryParse(sizes[0], out var width) && int.TryParse(sizes[1], out var height))
                                {
                                    currentTemplate.Width = width;
                                    currentTemplate.Height = height;
                                }
                                break;
                            case "Categories":
                                currentTemplate.Categories = value.Split(',').Select(cat => cat.Trim()).ToList();
                                break;
                            case "Tiles":
                                // The tiles are processed separately
                                break;
                            default:
                                if (int.TryParse(key, out var tileIndex))
                                {
                                    if (currentTemplate.Tiles == null)
                                        currentTemplate.Tiles = new Dictionary<int, string>();

                                    currentTemplate.Tiles[tileIndex] = value;
                                }
                                break;
                        }
                    }
                }
            }

            return templates;
        }

        static void ExportTemplateInfo(ushort id, TemplateInfo template, string outputDir)
        {
            // Export template information as text
            var infoPath = Path.Combine(outputDir, $"template_{id}.txt");
            using (var writer = new StreamWriter(infoPath))
            {
                writer.WriteLine($"Template@{id}:");
                writer.WriteLine($"\tId: {id}");
                writer.WriteLine($"\tImages: {string.Join(", ", template.Images ?? new List<string>())}");
                writer.WriteLine($"\tSize: {template.Width},{template.Height}");

                if (template.Categories != null && template.Categories.Any())
                    writer.WriteLine($"\tCategories: {string.Join(", ", template.Categories)}");

                writer.WriteLine("\tTiles:");

                if (template.Tiles != null)
                {
                    foreach (var tile in template.Tiles)
                    {
                        writer.WriteLine($"\t\t{tile.Key}: {tile.Value}");
                    }
                }
            }
        }

        static void ExportTemplateImageSimple(ushort id, TemplateInfo template, string outputDir)
        {
            // Calculate the dimensions of the template in pixels
            int cellSize = 24; // Default OpenRA cell size
            int width = template.Width * cellSize;
            int height = template.Height * cellSize;

            // Create the output image using System.Drawing
            using (var bitmap = new Bitmap(width, height))
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.LightGray); // Background color

                // Draw grid lines
                using (var gridPen = new Pen(Color.Gray))
                {
                    for (int x = 0; x <= template.Width; x++)
                        g.DrawLine(gridPen, x * cellSize, 0, x * cellSize, height);

                    for (int y = 0; y <= template.Height; y++)
                        g.DrawLine(gridPen, 0, y * cellSize, width, y * cellSize);
                }

                // Render each cell in the template
                if (template.Tiles != null)
                {
                    for (int y = 0; y < template.Height; y++)
                    {
                        for (int x = 0; x < template.Width; x++)
                        {
                            int cellIndex = y * template.Width + x;
                            if (template.Tiles.TryGetValue(cellIndex, out var tileType))
                            {
                                // Render a basic colored rectangle for the tile type
                                var color = GetColorForTileType(tileType);
                                var rect = new Rectangle(x * cellSize + 1, y * cellSize + 1, cellSize - 2, cellSize - 2);
                                g.FillRectangle(new SolidBrush(color), rect);

                                // Draw the tile type as text
                                var textBrush = new SolidBrush(Color.Black);
                                var font = new Font("Arial", 7);
                                g.DrawString(tileType, font, textBrush, rect.X + 2, rect.Y + 2);

                                // Draw the index
                                var smallFont = new Font("Arial", 6);
                                g.DrawString(cellIndex.ToString(), smallFont, new SolidBrush(Color.DarkBlue),
                                    rect.X + rect.Width - 15, rect.Y + rect.Height - 15);
                            }
                        }
                    }
                }

                // Draw outer border
                using (var borderPen = new Pen(Color.Black, 2))
                {
                    g.DrawRectangle(borderPen, 0, 0, width - 1, height - 1);
                }

                // Save the image
                var outputPath = Path.Combine(outputDir, $"template_{id}.png");
                bitmap.Save(outputPath, ImageFormat.Png);
            }
        }

        static Color GetColorForTileType(string tileType)
        {
            // Map tile types to specific colors for visualization
            if (string.IsNullOrEmpty(tileType))
                return Color.White;

            switch (tileType.ToLowerInvariant())
            {
                case "clear": return Color.White;
                case "road": return Color.Gray;
                case "water": return Color.Blue;
                case "river": return Color.LightBlue;
                case "rock": return Color.DarkGray;
                case "rough": return Color.SandyBrown;
                case "cliff": return Color.Brown;
                case "beach": return Color.Yellow;
                default: return Color.ForestGreen;
            }
        }
    }

    class TemplateInfo
    {
        public List<string> Images { get; set; } = new List<string>();
        public int Width { get; set; }
        public int Height { get; set; }
        public List<string> Categories { get; set; } = new List<string>();
        public Dictionary<int, string> Tiles { get; set; } = new Dictionary<int, string>();
    }
}
