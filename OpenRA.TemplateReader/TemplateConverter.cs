using System;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;
using System.Linq;
using SixLabors.ImageSharp.Drawing;

namespace OpenRA.TemplateReader
{
    public class TemplateConverter
    {
        // Template files are typically 24x24 pixels per cell
        private const int CellWidth = 24;
        private const int CellHeight = 24;

        public Image<Rgba32> ConvertTemplateToImage(byte[] templateData)
        {
            if (templateData == null || templateData.Length < 2)
                return null;

            try
            {
                using (var ms = new MemoryStream(templateData))
                {
                    // Read template width and height from the first two bytes
                    int width = ms.ReadByte();
                    int height = ms.ReadByte();

                    // Validate dimensions
                    if (width <= 0 || height <= 0 || width > 64 || height > 64)
                    {
                        Console.WriteLine($"Invalid template dimensions: {width}x{height}");
                        return null;
                    }

                    // Format detection logic
                    if (IsTemplateCncFormat(templateData))
                    {
                        return ConvertCncTemplate(templateData, width, height);
                    }
                    else
                    {
                        return ConvertRaTemplate(templateData, width, height);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error converting template data to image: {ex.Message}");
                return null;
            }
        }

        public Image<Rgba32> ConvertTemplateToImage(byte[] templateData, int width, int height)
        {
            if (templateData == null || templateData.Length == 0)
                return null;

            try
            {
                // Format detection logic
                if (IsTemplateCncFormat(templateData))
                {
                    return ConvertCncTemplate(templateData, width, height);
                }
                else
                {
                    return ConvertRaTemplate(templateData, width, height);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error converting template data to image: {ex.Message}");
                return null;
            }
        }

        private bool IsTemplateCncFormat(byte[] data)
        {
            // Very simple heuristic - C&C templates typically start with a specific header
            // This is a simplification and might need refinement
            return data.Length > 4 && data[0] == 0x00 && data[1] == 0x00;
        }

        private Image<Rgba32> ConvertRaTemplate(byte[] data, int width, int height)
        {
            var image = new Image<Rgba32>(width * CellWidth, height * CellHeight);

            using (var ms = new MemoryStream(data))
            using (var br = new BinaryReader(ms))
            {
                // Skip the header (width and height) if it's at the beginning
                if (data.Length > 2 && data[0] > 0 && data[0] <= 64 && data[1] > 0 && data[1] <= 64)
                {
                    ms.Position = 2;
                }
                else
                {
                    ms.Position = 0;
                }

                // Process each tile in the template
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        if (ms.Position + 1 >= ms.Length)
                            break;

                        // Read terrain type and height
                        byte terrainType = (byte)ms.ReadByte();
                        byte tileHeight = (byte)ms.ReadByte();

                        // Check for ramp info
                        byte rampType = 0;
                        if ((tileHeight & 0x80) != 0)  // If high bit is set, it's a ramp
                        {
                            rampType = (byte)((tileHeight >> 4) & 0x07);
                            tileHeight = (byte)(tileHeight & 0x0F);
                        }

                        // Calculate position in the image
                        int tileX = x * CellWidth;
                        int tileY = y * CellHeight;

                        // Draw the tile
                        DrawTile(image, tileX, tileY, CellWidth, CellHeight, terrainType, tileHeight, rampType);
                    }
                }
            }

            return image;
        }

        private Image<Rgba32> ConvertCncTemplate(byte[] data, int width, int height)
        {
            var image = new Image<Rgba32>(width * CellWidth, height * CellHeight);

            // Similar to RA template handling but with C&C specific format
            // This is a simplified implementation

            image.Mutate(ctx =>
            {
                ctx.Fill(Color.Transparent);

                // Draw a grid to show the template structure
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        // Use the terrain type from the data if available
                        byte terrainType = 0;
                        byte tileHeight = 0;

                        if (data.Length > 2 + (y * width + x) * 2 + 1)
                        {
                            terrainType = data[2 + (y * width + x) * 2];
                            tileHeight = data[2 + (y * width + x) * 2 + 1];
                        }

                        DrawTile(image, x * CellWidth, y * CellHeight, CellWidth, CellHeight, terrainType, tileHeight, 0);
                    }
                }
            });

            return image;
        }

        private void DrawTile(Image<Rgba32> image, int x, int y, int width, int height, byte terrainType, byte tileHeight, byte rampType)
        {
            // Calculate color based on terrain type and height
            Color tileColor = GetTerrainColor(terrainType, tileHeight);

            // Draw the tile
            image.Mutate(ctx =>
            {
                // Fill the tile with the base color
                ctx.Fill(tileColor, new Rectangle(x, y, width, height));

                // Draw a border around the tile
                ctx.Draw(new Color(new Rgba32(0, 0, 0, 100)), 1, new Rectangle(x, y, width, height));

                // If it's a ramp, indicate that
                if (rampType > 0)
                {
                    // Draw diagonal line to indicate ramp
                    ctx.DrawLines(Color.Red, 2, new PointF(x, y + height), new PointF(x + width, y));
                }
            });
        }

        private Color GetTerrainColor(byte terrainType, byte height)
        {
            // Base color depends on terrain type
            byte r, g, b;

            switch (terrainType)
            {
                case 0: // Clear/Sand
                    r = (byte)(180 - height * 4);
                    g = (byte)(200 - height * 4);
                    b = (byte)(120 - height * 4);
                    break;
                case 1: // Rough/Dunes
                    r = (byte)(140 - height * 3);
                    g = (byte)(170 - height * 3);
                    b = (byte)(90 - height * 3);
                    break;
                case 2: // Rock
                    r = (byte)(150 - height * 4);
                    g = (byte)(140 - height * 4);
                    b = (byte)(120 - height * 4);
                    break;
                case 3: // Road/Cliff
                    r = (byte)(170 - height * 3);
                    g = (byte)(160 - height * 3);
                    b = (byte)(140 - height * 3);
                    break;
                case 4: // Water
                    r = (byte)(100 - height * 2);
                    g = (byte)(130 - height * 2);
                    b = (byte)(190 - height * 2);
                    break;
                default:
                    r = (byte)(150 - height * 4);
                    g = (byte)(180 - height * 4);
                    b = (byte)(110 - height * 4);
                    break;
            }

            return new Color(new Rgba32(r, g, b, 255));
        }
    }
}
