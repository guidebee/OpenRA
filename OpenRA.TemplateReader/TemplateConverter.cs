using System;
using System.Collections.Generic;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;
using System.Linq;
using SixLabors.ImageSharp.Drawing;

namespace OpenRA.TemplateReader
{
    /// <summary>
    /// Represents a simplified version of TerrainTemplateInfo for use in the template reader
    /// </summary>
    public class TemplateInfo
    {
        public ushort Id { get; set; }
        public string[] Images { get; set; }
        public int2 Size { get; set; }
        public string[] Categories { get; set; }
        public Dictionary<int, string> Tiles { get; set; } = new Dictionary<int, string>();

        public TemplateInfo()
        {
            // Default values
            Id = 0;
            Images = new[] { string.Empty };
            Size = new int2(1, 1);
            Categories = new[] { "Unknown" };
        }

        public TemplateInfo(ushort id, string imageName, int width, int height)
        {
            Id = id;
            Images = new[] { imageName };
            Size = new int2(width, height);
            Categories = new[] { "Unknown" };
        }
    }

    public class TemplateConverter
    {
        // Template files are typically 24x24 pixels per cell (same as in game)
        private const int CellWidth = 24;
        private const int CellHeight = 24;

        // Use the same scale as in TerrainTemplatePreviewWidget
        private const float DefaultScale = 1.0f;

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

        /// <summary>
        /// Convert a template to an image based on an existing template info definition (from yaml)
        /// This uses the image name from the template info instead of raw template data
        /// </summary>
        public Image<Rgba32> ConvertTemplateToImage(TemplateInfo templateInfo, Func<string, byte[]> imageLoader)
        {
            if (templateInfo == null || templateInfo.Images == null || templateInfo.Images.Length == 0 || string.IsNullOrEmpty(templateInfo.Images[0]))
            {
                Console.WriteLine("Invalid template info or missing images");
                return null;
            }

            try
            {
                // Similar to how TerrainTemplatePreviewWidget works, use the first image in the Images array
                var imageName = templateInfo.Images[0];
                Console.WriteLine($"Loading template image: {imageName}");

                // Load the image data using the provided loader function
                var imageData = imageLoader(imageName);
                if (imageData == null || imageData.Length < 2)
                {
                    Console.WriteLine($"Failed to load image data for {imageName}");

                    // Create a generic placeholder for missing template images
                    // This is better than having special cases for specific templates
                    return CreatePlaceholderTemplate(templateInfo.Size.X, templateInfo.Size.Y, templateInfo.Id);
                }

                // Create a template with the proper dimensions from the template info
                return ConvertTemplateToImage(imageData, templateInfo.Size.X, templateInfo.Size.Y);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error converting template info to image: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Creates a placeholder template image for when the original assets are not available
        /// </summary>
        public Image<Rgba32> CreatePlaceholderTemplate(int width, int height, ushort templateId)
        {
            // Calculate template bounds as normal
            var templateRect = CalculateTemplateBounds(width, height);

            // Create image with appropriate size
            int imageWidth = (int)(templateRect.Width * DefaultScale);
            int imageHeight = (int)(templateRect.Height * DefaultScale);
            var image = new Image<Rgba32>(imageWidth, imageHeight);

            // Determine the type of template based on the ID to create a more appropriate placeholder
            Color baseColor;

            // Categorize templates by ID ranges - this is a simplification
            if (templateId >= 100 && templateId < 120) // River templates
            {
                // Use a blue color for river templates
                baseColor = new Color(new Rgba32(82, 126, 185, 220));
            }
            else if (templateId >= 200 && templateId < 220) // Shore templates
            {
                // Use a sand color for shore templates
                baseColor = new Color(new Rgba32(220, 202, 142, 255));
            }
            else if (templateId >= 300 && templateId < 350) // Rock templates
            {
                // Use a gray color for rock templates
                baseColor = new Color(new Rgba32(142, 128, 96, 255));
            }
            else
            {
                // Default placeholder color
                baseColor = new Color(new Rgba32(200, 196, 164, 255));
            }

            image.Mutate(ctx =>
            {
                // Fill with base color
                ctx.Fill(baseColor, new Rectangle(0, 0, imageWidth, imageHeight));

                // Add some texture details based on template type
                if (templateId >= 100 && templateId < 120) // River templates
                {
                    // Add some river details
                    ctx.Fill(new Color(new Rgba32(116, 140, 196, 230)),
                        new Rectangle(imageWidth/4, imageHeight/4, imageWidth/2, imageHeight/2));

                    // Add some white ripples for effect
                    ctx.Fill(new Color(new Rgba32(255, 255, 255, 40)),
                        new Rectangle(imageWidth/4, imageHeight/3, imageWidth/2, imageHeight/6));
                }

                // Draw a grid to represent cells
                for (int x = 0; x < width; x++)
                {
                    for (int y = 0; y < height; y++)
                    {
                        ctx.Draw(new Color(new Rgba32(0, 0, 0, 30)), 1,
                            new Rectangle(
                                (int)(x * CellWidth * DefaultScale),
                                (int)(y * CellHeight * DefaultScale),
                                (int)(CellWidth * DefaultScale),
                                (int)(CellHeight * DefaultScale)));
                    }
                }

                // Add a label showing this is a placeholder
                ctx.Fill(new Color(new Rgba32(255, 255, 255, 180)),
                    new Rectangle(10, 10, imageWidth - 20, 20));

                // Add template ID indication in the center
                var centerRect = new Rectangle(
                    imageWidth/4, imageHeight/3,
                    imageWidth/2, imageHeight/4);

                ctx.Fill(new Color(new Rgba32(0, 0, 0, 60)), centerRect);

                // Note: In a real implementation, you would use DrawText to add template ID
                // but we're using shapes for simplicity
            });

            return image;
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
            // First determine the template bounds, similar to terrainRenderer.TemplateBounds
            var templateRect = CalculateTemplateBounds(width, height);

            // Create image with appropriate size (matching what the TerrainTemplatePreviewWidget would use)
            int imageWidth = (int)(templateRect.Width * DefaultScale);
            int imageHeight = (int)(templateRect.Height * DefaultScale);
            var image = new Image<Rgba32>(imageWidth, imageHeight);

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

                image.Mutate(ctx => ctx.Fill(Color.Transparent)); // Start with transparent background

                // Process each tile in the template (like RenderUIPreview does)
                int i = 0;
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++, i++)
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

                        // Calculate cell position using the same grid logic as in TerrainTemplatePreviewWidget
                        float u = x;  // For rectangular grid
                        float v = y;

                        // Calculate sprite position (similar to TerrainRenderer.RenderUIPreview)
                        var tl = new PointF(
                            u * CellWidth - templateRect.Left,
                            (v - 0.5f * tileHeight / 15f) * CellHeight - templateRect.Top);

                        // Apply scale
                        tl = new PointF(tl.X * DefaultScale, tl.Y * DefaultScale);

                        // Draw the tile
                        DrawTile(image, (int)tl.X, (int)tl.Y,
                                (int)(CellWidth * DefaultScale),
                                (int)(CellHeight * DefaultScale),
                                terrainType, tileHeight, rampType);
                    }
                }
            }

            return image;
        }

        // Similar to the TemplateBounds method in TerrainRenderer
        private Rectangle CalculateTemplateBounds(int width, int height)
        {
            // Simplification of TemplateBounds calculation
            // This calculates the bounds of the template for rendering
            int left = 0;
            int top = 0;
            int right = width * CellWidth;
            int bottom = height * CellHeight;

            // Return the bounds
            return new Rectangle(left, top, right - left, bottom - top);
        }

        private Image<Rgba32> ConvertCncTemplate(byte[] data, int width, int height)
        {
            // Use the same template bounds approach as for RA templates
            var templateRect = CalculateTemplateBounds(width, height);

            // Create image with appropriate size
            int imageWidth = (int)(templateRect.Width * DefaultScale);
            int imageHeight = (int)(templateRect.Height * DefaultScale);
            var image = new Image<Rgba32>(imageWidth, imageHeight);

            image.Mutate(ctx => ctx.Fill(Color.Transparent));

            // C&C templates have a different format but we'll process them in a grid-like manner
            // for consistency with the RA template rendering

            using (var ms = new MemoryStream(data))
            using (var br = new BinaryReader(ms))
            {
                // Skip the header if needed
                if (data.Length > 2 && data[0] == 0x00 && data[1] == 0x00)
                {
                    ms.Position = 2;
                }

                // Process each tile in the template
                int i = 0;
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++, i++)
                    {
                        // Calculate grid position
                        float u = x;
                        float v = y;

                        // Use terrain type from the data if available
                        byte terrainType = 0;
                        byte tileHeight = 0;

                        // Read data from the template if possible
                        if (ms.Position + 1 < ms.Length)
                        {
                            terrainType = (byte)ms.ReadByte();
                            tileHeight = (byte)ms.ReadByte();
                        }

                        // Calculate position
                        var tl = new PointF(
                            u * CellWidth - templateRect.Left,
                            (v - 0.5f * tileHeight / 15f) * CellHeight - templateRect.Top);

                        // Apply scale
                        tl = new PointF(tl.X * DefaultScale, tl.Y * DefaultScale);

                        // Draw the tile
                        DrawTile(image, (int)tl.X, (int)tl.Y,
                                (int)(CellWidth * DefaultScale),
                                (int)(CellHeight * DefaultScale),
                                terrainType, tileHeight, 0);
                    }
                }
            }

            return image;
        }

        private void DrawTile(Image<Rgba32> image, int x, int y, int width, int height, byte terrainType, byte tileHeight, byte rampType)
        {
            // Calculate color based on terrain type and height, matching game's appearance more closely
            Color tileColor = GetTerrainColor(terrainType, tileHeight);

            // Draw the tile (similar to UISpriteRenderable in TerrainRenderer.RenderUIPreview)
            image.Mutate(ctx =>
            {
                // Fill the tile with the base color
                ctx.Fill(tileColor, new Rectangle(x, y, width, height));

                // Draw terrain features based on type
                switch (terrainType)
                {
                    case 0: // Clear/Sand - add slight texture
                        ctx.Fill(new Color(new Rgba32(255, 255, 255, 20)),
                                new Rectangle(x, y, width/2, height/2));
                        break;
                    case 1: // Rough/Dunes - add small bumps
                        ctx.Fill(new Color(new Rgba32(255, 255, 255, 20)),
                                new EllipsePolygon(x + width/4, y + height/4, width/8));
                        ctx.Fill(new Color(new Rgba32(255, 255, 255, 20)),
                                new EllipsePolygon(x + 3*width/4, y + 3*height/4, width/8));
                        break;
                    case 2: // Rock - add rock texture
                        ctx.Fill(new Color(new Rgba32(60, 60, 60, 30)),
                                new Rectangle(x + width/4, y + height/4, width/2, height/2));
                        break;
                    case 4: // Water - add wave effect
                        ctx.Fill(new Color(new Rgba32(255, 255, 255, 25)),
                                new Rectangle(x, y + height/3, width, height/6));
                        break;
                }

                // If it's a ramp, indicate that with direction
                if (rampType > 0)
                {
                    // Draw arrow indicating ramp direction
                    switch (rampType)
                    {
                        case 1: // North
                            ctx.DrawLines(Color.White, 2,
                                new PointF(x + width/2, y + height*3/4),
                                new PointF(x + width/2, y + height/4),
                                new PointF(x + width/3, y + height/3));
                            ctx.DrawLines(Color.White, 2,
                                new PointF(x + width/2, y + height/4),
                                new PointF(x + width*2/3, y + height/3));
                            break;
                        case 2: // East
                            ctx.DrawLines(Color.White, 2,
                                new PointF(x + width/4, y + height/2),
                                new PointF(x + width*3/4, y + height/2),
                                new PointF(x + width*2/3, y + height/3));
                            ctx.DrawLines(Color.White, 2,
                                new PointF(x + width*3/4, y + height/2),
                                new PointF(x + width*2/3, y + height*2/3));
                            break;
                        default:
                            // Default diagonal indicator
                            ctx.DrawLines(Color.White, 2,
                                new PointF(x, y + height),
                                new PointF(x + width, y));
                            break;
                    }
                }

                // Draw a subtle grid line to show cell boundaries
                ctx.Draw(new Color(new Rgba32(0, 0, 0, 40)), 1, new Rectangle(x, y, width, height));

                // Add height indicator - brighter for higher tiles
                if (tileHeight > 0)
                {
                    var heightText = tileHeight.ToString();
                    // Draw a small indicator in corner showing height
                    ctx.Fill(new Color(new Rgba32(255, 255, 255, 150)),
                            new RectangularPolygon(x + width - 12, y + 2, 10, 10));
                    // Note: In a real implementation, you would use DrawText, but for simplicity we're using shapes
                }
            });
        }

        private Color GetTerrainColor(byte terrainType, byte height)
        {
            // Enhanced color scheme matching the game's appearance more closely
            // Use a palette similar to what OpenRA uses

            byte r, g, b;
            byte alpha = 255; // Fully opaque

            // Base color depends on terrain type
            switch (terrainType)
            {
                case 0: // Clear/Sand
                    r = 238;
                    g = 214;
                    b = 156;
                    break;
                case 1: // Rough/Dunes
                    r = 206;
                    g = 192;
                    b = 132;
                    break;
                case 2: // Rock
                    r = 142;
                    g = 128;
                    b = 96;
                    break;
                case 3: // Road/Cliff
                    r = 176;
                    g = 166;
                    b = 146;
                    break;
                case 4: // Water
                    r = 82;
                    g = 126;
                    b = 185;
                    alpha = 220; // Slightly transparent for water
                    break;
                case 5: // River
                    r = 116;
                    g = 140;
                    b = 196;
                    alpha = 230;
                    break;
                case 6: // Shore
                    r = 220;
                    g = 202;
                    b = 142;
                    break;
                default:
                    r = 200;
                    g = 196;
                    b = 164;
                    break;
            }

            // Modify the color based on height (similar to how the game does it)
            float heightFactor = height / 15.0f; // Normalize height (usually 0-15)

            // For higher terrain, make it slightly darker and more saturated
            if (height > 0)
            {
                r = (byte)Math.Max(0, r - height * 4);
                g = (byte)Math.Max(0, g - height * 4);
                b = (byte)Math.Max(0, b - height * 4);
            }

            return new Color(new Rgba32(r, g, b, alpha));
        }
    }
}
