using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using OpenRA.FileSystem;
using OpenRA.Primitives;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing;
using SixLabors.Fonts;
using Rectangle = SixLabors.ImageSharp.Rectangle;
using Color = SixLabors.ImageSharp.Color;
using PointF = SixLabors.ImageSharp.PointF;
using Path = System.IO.Path;

namespace OpenRA.MapReader
{
    /// <summary>
    /// Template information structure for JSON export
    /// </summary>
    public class TemplateExportInfo
    {
        public ushort Id { get; set; }
        public int2 Size { get; set; }
        public bool PickAny { get; set; }
        public string[] Categories { get; set; }
        public string[] Images { get; set; }
        public string[] DepthImages { get; set; }
        public int[] Frames { get; set; }
        public string Palette { get; set; }
        public List<TemplateTileExportInfo> Tiles { get; set; } = new List<TemplateTileExportInfo>();
    }

    /// <summary>
    /// Tile information structure for JSON export
    /// </summary>
    public class TemplateTileExportInfo
    {
        public int Index { get; set; }
        public byte TerrainType { get; set; }
        public byte Height { get; set; }
        public byte RampType { get; set; }
        public int[] MinColor { get; set; }
        public int[] MaxColor { get; set; }
    }

    class Program
    {
        static void DrawLine(Image<Rgba32> image, int x0, int y0, int x1, int y1, Rgba32 color)
        {
            int dx = Math.Abs(x1 - x0);
            int dy = Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            while (true)
            {
                if (x0 >= 0 && x0 < image.Width && y0 >= 0 && y0 < image.Height)
                {
                    image[x0, y0] = color;
                    for (int t = -1; t <= 1; t++)
                    {
                        for (int s = -1; s <= 1; s++)
                        {
                            int px = x0 + t;
                            int py = y0 + s;
                            if (px >= 0 && px < image.Width && py >= 0 && py < image.Height)
                                image[px, py] = color;
                        }
                    }
                }

                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 > -dy)
                {
                    err -= dy;
                    x0 += sx;
                }
                if (e2 < dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }
        }

        static void DrawArrow(Image<Rgba32> image, int x, int y, int size, int angleDegrees)
        {
            double angleRadians = angleDegrees * Math.PI / 180;
            var arrowColor = new Rgba32(255, 255, 255, 180);

            int tipX = x + (int)(Math.Sin(angleRadians) * size);
            int tipY = y - (int)(Math.Cos(angleRadians) * size);

            int leftX = x + (int)(Math.Sin(angleRadians - 0.5) * (size * 0.7f));
            int leftY = y - (int)(Math.Cos(angleRadians - 0.5) * (size * 0.7f));

            int rightX = x + (int)(Math.Sin(angleRadians + 0.5) * (size * 0.7f));
            int rightY = y - (int)(Math.Cos(angleRadians + 0.5) * (size * 0.7f));

            DrawLine(image, x, y, tipX, tipY, arrowColor);
            DrawLine(image, tipX, tipY, leftX, leftY, arrowColor);
            DrawLine(image, tipX, tipY, rightX, rightY, arrowColor);
        }

        static void DrawRamp(Image<Rgba32> image, int x, int y, int size, byte rampType)
        {
            var arrowColor = new Rgba32(255, 255, 255, 180);
            int arrowSize = size / 3;
            int centerX = x + size / 2;
            int centerY = y + size / 2;

            switch (rampType)
            {
                case 1:
                    DrawArrow(image, centerX, centerY, arrowSize, 0);
                    break;
                case 2:
                    DrawArrow(image, centerX, centerY, arrowSize, 90);
                    break;
                case 3:
                    DrawArrow(image, centerX, centerY, arrowSize, 180);
                    break;
                case 4:
                    DrawArrow(image, centerX, centerY, arrowSize, 270);
                    break;
                case 5:
                    DrawArrow(image, centerX, centerY, arrowSize, 45);
                    break;
                case 6:
                    DrawArrow(image, centerX, centerY, arrowSize, 135);
                    break;
                case 7:
                    DrawArrow(image, centerX, centerY, arrowSize, 225);
                    break;
                case 8:
                    DrawArrow(image, centerX, centerY, arrowSize, 315);
                    break;
            }
        }

        static void DrawWater(Image<Rgba32> image, int x, int y, int size)
        {
            var waterColor = new Rgba32(64, 147, 206, 255);
            var waveColor1 = new Rgba32(100, 183, 242, 255);
            var waveColor2 = new Rgba32(32, 125, 175, 255);

            image.Mutate(ctx =>
            {
                ctx.Fill(waterColor, new Rectangle(x, y, size, size));

                var random = new Random((x + y) * 1000);

                for (int i = 0; i < 5; i++)
                {
                    int waveY = y + random.Next(size);
                    int amplitude = random.Next(2, 5);
                    int frequency = random.Next(10, 20);
                    bool useLight = random.Next(2) == 0;

                    for (int wx = 0; wx < size; wx += 2)
                    {
                        double angle = (wx + x) / (double)frequency;
                        int offset = (int)(Math.Sin(angle) * amplitude);

                        for (int d = 0; d < 3; d++)
                        {
                            int px = x + wx + d;
                            int py = waveY + offset;

                            if (px >= x && px < x + size && py >= y && py < y + size)
                                image[px, py] = useLight ? waveColor1 : waveColor2;
                        }
                    }
                }

                for (int py = y + size - 10; py < y + size; py++)
                {
                    var blendFactor = 0.7f - 0.7f * (y + size - py) / 10f;
                    var reflectionColor = new Rgba32(
                        (byte)(waterColor.R + (255 - waterColor.R) * blendFactor),
                        (byte)(waterColor.G + (255 - waterColor.G) * blendFactor),
                        (byte)(waterColor.B + (255 - waterColor.B) * blendFactor),
                        255);

                    for (int px = x; px < x + size; px++)
                    {
                        if (random.Next(10) > 3)
                            image[px, py] = reflectionColor;
                    }
                }
            });
        }

        static void DrawIce(Image<Rgba32> image, int x, int y, int size)
        {
            var iceColor = new Rgba32(188, 232, 245, 255);
            var crackColor = new Rgba32(220, 240, 255, 255);
            var shadowColor = new Rgba32(150, 200, 220, 255);

            image.Mutate(ctx =>
            {
                ctx.Fill(iceColor, new Rectangle(x, y, size, size));

                var random = new Random((x + y) * 1000);

                for (int py = y; py < y + size; py++)
                {
                    for (int px = x; px < x + size; px++)
                    {
                        if (random.Next(100) < 15)
                        {
                            var variation = random.Next(-10, 10);
                            image[px, py] = new Rgba32(
                                (byte)Math.Clamp(iceColor.R + variation, 0, 255),
                                (byte)Math.Clamp(iceColor.G + variation, 0, 255),
                                (byte)Math.Clamp(iceColor.B + variation, 0, 255),
                                255);
                        }
                    }
                }

                for (int i = 0; i < 3; i++)
                {
                    int startX = x + random.Next(size);
                    int startY = y + random.Next(size);
                    int length = random.Next(10, 30);
                    double angle = random.NextDouble() * Math.PI * 2;

                    int endX = (int)(startX + Math.Cos(angle) * length);
                    int endY = (int)(startY + Math.Sin(angle) * length);

                    DrawLine(image, startX, startY, endX, endY, crackColor);

                    int branches = random.Next(1, 3);
                    for (int b = 0; b < branches; b++)
                    {
                        int branchX = startX + (endX - startX) * random.Next(30, 70) / 100;
                        int branchY = startY + (endY - startY) * random.Next(30, 70) / 100;

                        double branchAngle = angle + (random.NextDouble() - 0.5) * Math.PI / 2;
                        int branchLength = random.Next(5, 15);

                        int branchEndX = (int)(branchX + Math.Cos(branchAngle) * branchLength);
                        int branchEndY = (int)(branchY + Math.Sin(branchAngle) * branchLength);

                        DrawLine(image, branchX, branchY, branchEndX, branchEndY, crackColor);
                    }
                }

                for (int i = 0; i < 3; i++)
                {
                    for (int px = x; px < x + size; px++)
                        image[px, y + i] = shadowColor;

                    for (int px = x; px < x + size; px++)
                        image[px, y + size - 1 - i] = shadowColor;

                    for (int py = y; py < y + size; py++)
                        image[x + i, py] = shadowColor;

                    for (int py = y; py < y + size; py++)
                        image[x + size - 1 - i, py] = shadowColor;
                }
            });
        }

        static void DrawWall(Image<Rgba32> image, int x, int y, int size)
        {
            var wallColor = new Rgba32(100, 100, 100, 255);
            var shadowColor = new Rgba32(70, 70, 70, 255);
            var highlightColor = new Rgba32(150, 150, 150, 255);
            var crackColor = new Rgba32(60, 60, 60, 255);
            var baseColor = new Rgba32(120, 120, 120, 255);

            image.Mutate(ctx =>
            {
                ctx.Fill(baseColor, new Rectangle(x, y, size, size));

                var random = new Random((x + y) * 1000);

                // Determine if the wall is horizontal, vertical, or a corner/junction
                bool leftConnection = false;
                bool rightConnection = false;
                bool topConnection = false;
                bool bottomConnection = false;

                // Check surrounding pixels to determine wall connections
                if (x > 0 && y > 0 && x + size < image.Width && y + size < image.Height)
                {
                    Rgba32 leftPixel = image[x - 1, y + size / 2];
                    Rgba32 rightPixel = image[x + size, y + size / 2];
                    Rgba32 topPixel = image[x + size / 2, y - 1];
                    Rgba32 bottomPixel = image[x + size / 2, y + size];

                    leftConnection = ColorMatch(leftPixel, wallColor) || ColorMatch(leftPixel, baseColor);
                    rightConnection = ColorMatch(rightPixel, wallColor) || ColorMatch(rightPixel, baseColor);
                    topConnection = ColorMatch(topPixel, wallColor) || ColorMatch(topPixel, baseColor);
                    bottomConnection = ColorMatch(bottomPixel, wallColor) || ColorMatch(bottomPixel, baseColor);
                }

                bool horizontal = (leftConnection || rightConnection) && !(topConnection || bottomConnection);
                bool vertical = (topConnection || bottomConnection) && !(leftConnection || rightConnection);
                bool corner = (leftConnection || rightConnection) && (topConnection || bottomConnection);

                int wallWidth;

                if (horizontal)
                {
                    // Horizontal wall
                    wallWidth = size / 3;
                    int wallY = y + (size - wallWidth) / 2;

                    for (int py = wallY; py < wallY + wallWidth; py++)
                    {
                        for (int px = x; px < x + size; px++)
                        {
                            image[px, py] = wallColor;

                            // Add highlight to top edge
                            if (py == wallY)
                            {
                                image[px, py] = highlightColor;
                            }
                            // Add shadow to bottom edge
                            else if (py == wallY + wallWidth - 1)
                            {
                                image[px, py] = shadowColor;
                            }
                        }
                    }

                    // Add wall bricks/texture
                    for (int bx = 0; bx < size / 10; bx++)
                    {
                        int brickX = x + bx * 10 + random.Next(3);
                        int brickWidth = random.Next(6, 9);

                        for (int px = brickX; px < brickX + brickWidth && px < x + size; px++)
                        {
                            // Top brick row
                            for (int py = wallY + 2; py < wallY + wallWidth / 2 - 1; py++)
                            {
                                int colorVar = random.Next(-10, 11);
                                image[px, py] = new Rgba32(
                                    (byte)Math.Clamp(wallColor.R + colorVar, 0, 255),
                                    (byte)Math.Clamp(wallColor.G + colorVar, 0, 255),
                                    (byte)Math.Clamp(wallColor.B + colorVar, 0, 255),
                                    255);
                            }

                            // Bottom brick row (offset)
                            for (int py = wallY + wallWidth / 2 + 1; py < wallY + wallWidth - 2; py++)
                            {
                                int colorVar = random.Next(-10, 11);
                                image[px, py] = new Rgba32(
                                    (byte)Math.Clamp(wallColor.R + colorVar, 0, 255),
                                    (byte)Math.Clamp(wallColor.G + colorVar, 0, 255),
                                    (byte)Math.Clamp(wallColor.B + colorVar, 0, 255),
                                    255);
                            }
                        }
                    }
                }
                else if (vertical)
                {
                    // Vertical wall
                    wallWidth = size / 3;
                    int wallX = x + (size - wallWidth) / 2;

                    for (int px = wallX; px < wallX + wallWidth; px++)
                    {
                        for (int py = y; py < y + size; py++)
                        {
                            image[px, py] = wallColor;

                            // Add shadow to left edge
                            if (px == wallX)
                            {
                                image[px, py] = shadowColor;
                            }
                            // Add highlight to right edge
                            else if (px == wallX + wallWidth - 1)
                            {
                                image[px, py] = highlightColor;
                            }
                        }
                    }

                    // Add wall bricks/texture
                    for (int by = 0; by < size / 10; by++)
                    {
                        int brickY = y + by * 10 + random.Next(3);
                        int brickHeight = random.Next(6, 9);

                        for (int py = brickY; py < brickY + brickHeight && py < y + size; py++)
                        {
                            // Left brick row
                            for (int px = wallX + 2; px < wallX + wallWidth / 2 - 1; px++)
                            {
                                int colorVar = random.Next(-10, 11);
                                image[px, py] = new Rgba32(
                                    (byte)Math.Clamp(wallColor.R + colorVar, 0, 255),
                                    (byte)Math.Clamp(wallColor.G + colorVar, 0, 255),
                                    (byte)Math.Clamp(wallColor.B + colorVar, 0, 255),
                                    255);
                            }

                            // Right brick row (offset)
                            for (int px = wallX + wallWidth / 2 + 1; px < wallX + wallWidth - 2; px++)
                            {
                                int colorVar = random.Next(-10, 11);
                                image[px, py] = new Rgba32(
                                    (byte)Math.Clamp(wallColor.R + colorVar, 0, 255),
                                    (byte)Math.Clamp(wallColor.G + colorVar, 0, 255),
                                    (byte)Math.Clamp(wallColor.B + colorVar, 0, 255),
                                    255);
                            }
                        }
                    }
                }
                else
                {
                    // Corner or junction wall (or isolated wall section)
                    wallWidth = size / 2;
                    int centerX = x + size / 2;
                    int centerY = y + size / 2;

                    for (int py = centerY - wallWidth / 2; py < centerY + wallWidth / 2; py++)
                    {
                        for (int px = centerX - wallWidth / 2; px < centerX + wallWidth / 2; px++)
                        {
                            image[px, py] = wallColor;

                            // Add texture
                            if (random.Next(100) < 30)
                            {
                                int colorVar = random.Next(-15, 16);
                                image[px, py] = new Rgba32(
                                    (byte)Math.Clamp(wallColor.R + colorVar, 0, 255),
                                    (byte)Math.Clamp(wallColor.G + colorVar, 0, 255),
                                    (byte)Math.Clamp(wallColor.B + colorVar, 0, 255),
                                    255);
                            }
                        }
                    }

                    // Add shadow and highlight for 3D effect
                    for (int py = centerY - wallWidth / 2; py < centerY + wallWidth / 2; py++)
                    {
                        image[centerX - wallWidth / 2, py] = shadowColor;
                        image[centerX + wallWidth / 2 - 1, py] = highlightColor;
                    }

                    for (int px = centerX - wallWidth / 2; px < centerX + wallWidth / 2; px++)
                    {
                        image[px, centerY - wallWidth / 2] = highlightColor;
                        image[px, centerY + wallWidth / 2 - 1] = shadowColor;
                    }

                    // Create connecting walls if needed
                    if (leftConnection)
                    {
                        for (int px = x; px < centerX - wallWidth / 2; px++)
                        {
                            for (int py = centerY - wallWidth / 4; py < centerY + wallWidth / 4; py++)
                            {
                                image[px, py] = wallColor;
                            }
                        }
                    }

                    if (rightConnection)
                    {
                        for (int px = centerX + wallWidth / 2; px < x + size; px++)
                        {
                            for (int py = centerY - wallWidth / 4; py < centerY + wallWidth / 4; py++)
                            {
                                image[px, py] = wallColor;
                            }
                        }
                    }

                    if (topConnection)
                    {
                        for (int py = y; py < centerY - wallWidth / 2; py++)
                        {
                            for (int px = centerX - wallWidth / 4; px < centerX + wallWidth / 4; px++)
                            {
                                image[px, py] = wallColor;
                            }
                        }
                    }

                    if (bottomConnection)
                    {
                        for (int py = centerY + wallWidth / 2; py < y + size; py++)
                        {
                            for (int px = centerX - wallWidth / 4; px < centerX + wallWidth / 4; px++)
                            {
                                image[px, py] = wallColor;
                            }
                        }
                    }
                }

                // Add cracks/damage to the wall
                int crackCount = random.Next(2, 5);
                for (int i = 0; i < crackCount; i++)
                {
                    int crackX = x + random.Next(size);
                    int crackY = y + random.Next(size);
                    int crackLength = random.Next(3, 8);
                    double angle = random.NextDouble() * Math.PI * 2;

                    for (int c = 0; c < crackLength; c++)
                    {
                        int cx = crackX + (int)(Math.Cos(angle) * c);
                        int cy = crackY + (int)(Math.Sin(angle) * c);

                        if (cx >= x && cx < x + size && cy >= y && cy < y + size)
                        {
                            var pixel = image[cx, cy];
                            if (ColorMatch(pixel, wallColor) || ColorMatch(pixel, shadowColor) || ColorMatch(pixel, highlightColor))
                            {
                                image[cx, cy] = crackColor;
                            }
                        }

                        // Slight angle change for natural crack look
                        angle += (random.NextDouble() - 0.5) * 0.5;
                    }
                }
            });
        }

        static void DrawRoughTerrain(Image<Rgba32> image, int x, int y, int size, Rgba32 baseColor)
        {
            var rockColor = new Rgba32(
                (byte)Math.Max(0, baseColor.R - 30),
                (byte)Math.Max(0, baseColor.G - 30),
                (byte)Math.Max(0, baseColor.B - 30),
                255);

            var highlightColor = new Rgba32(
                (byte)Math.Min(255, baseColor.R + 20),
                (byte)Math.Min(255, baseColor.G + 20),
                (byte)Math.Min(255, baseColor.B + 20),
                255);

            image.Mutate(ctx =>
            {
                ctx.Fill(baseColor, new Rectangle(x, y, size, size));

                var random = new Random((x + y) * 1000);

                for (int py = y; py < y + size; py++)
                {
                    for (int px = x; px < x + size; px++)
                    {
                        if (random.Next(100) < 30)
                        {
                            int variation = random.Next(-10, 11);
                            image[px, py] = new Rgba32(
                                (byte)Math.Clamp(baseColor.R + variation, 0, 255),
                                (byte)Math.Clamp(baseColor.G + variation, 0, 255),
                                (byte)Math.Clamp(baseColor.B + variation, 0, 255),
                                255);
                        }
                    }
                }

                for (int i = 0; i < 6; i++)
                {
                    int rockX = x + random.Next(size);
                    int rockY = y + random.Next(size);
                    int rockSize = random.Next(4, 9);

                    for (int ry = -rockSize; ry <= rockSize; ry++)
                    {
                        for (int rx = -rockSize; rx <= rockSize; rx++)
                        {
                            double distance = Math.Sqrt(rx * rx + ry * ry);
                            if (distance <= rockSize)
                            {
                                int px = rockX + rx;
                                int py = rockY + ry;

                                if (px >= x && px < x + size && py >= y && py < y + size)
                                {
                                    image[px, py] = rockColor;

                                    if (rx < 0 && ry < 0 && distance < rockSize - 1.5)
                                    {
                                        image[px, py] = highlightColor;
                                    }

                                    if (rx > 0 && ry > 0 && distance > rockSize - 2)
                                    {
                                        image[px, py] = new Rgba32(
                                            (byte)Math.Max(0, rockColor.R - 20),
                                            (byte)Math.Max(0, rockColor.G - 20),
                                            (byte)Math.Max(0, rockColor.B - 20),
                                            255);
                                    }
                                }
                            }
                        }
                    }
                }

                for (int i = 0; i < 15; i++)
                {
                    int pebbleX = x + random.Next(size);
                    int pebbleY = y + random.Next(size);
                    int pebbleSize = random.Next(1, 3);

                    for (int py = pebbleY - pebbleSize; py <= pebbleY + pebbleSize; py++)
                    {
                        for (int px = pebbleX - pebbleSize; px <= pebbleX + pebbleSize; px++)
                        {
                            double distance = Math.Sqrt((px - pebbleX) * (px - pebbleX) + (py - pebbleY) * (py - pebbleY));
                            if (distance <= pebbleSize)
                            {
                                if (px >= x && px < x + size && py >= y && py < y + size)
                                {
                                    image[px, py] = rockColor;
                                }
                            }
                        }
                    }
                }
            });
        }

        static void DrawRoad(Image<Rgba32> image, int x, int y, int size, Rgba32 roadColor)
        {
            var lineColor = new Rgba32(180, 180, 180, 255);
            var edgeColor = new Rgba32(
                (byte)Math.Max(0, roadColor.R - 20),
                (byte)Math.Max(0, roadColor.G - 20),
                (byte)Math.Max(0, roadColor.B - 20),
                255);

            image.Mutate(ctx =>
            {
                ctx.Fill(roadColor, new Rectangle(x, y, size, size));

                var random = new Random((x + y) * 1000);

                for (int py = y; py < y + size; py++)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        if (x + i >= 0 && x + i < image.Width)
                            image[x + i, py] = edgeColor;

                        if (x + size - 1 - i >= 0 && x + size - 1 - i < image.Width)
                            image[x + size - 1 - i, py] = edgeColor;
                    }
                }

                bool horizontalRoad = true;

                if (size > 30)
                {
                    int horizontalLineCount = 0;
                    int verticalLineCount = 0;

                    if (x > 0 && y > 0 && x + size < image.Width && y + size < image.Height)
                    {
                        Rgba32 leftPixel = image[x - 1, y + size / 2];
                        Rgba32 rightPixel = image[x + size, y + size / 2];

                        if (ColorMatch(leftPixel, roadColor) || ColorMatch(rightPixel, roadColor))
                            horizontalLineCount++;

                        Rgba32 topPixel = image[x + size / 2, y - 1];
                        Rgba32 bottomPixel = image[x + size / 2, y + size];

                        if (ColorMatch(topPixel, roadColor) || ColorMatch(bottomPixel, roadColor))
                            verticalLineCount++;
                    }

                    horizontalRoad = horizontalLineCount >= verticalLineCount;
                }

                if (horizontalRoad)
                {
                    int centerY = y + size / 2;

                    bool dashedLine = random.Next(2) == 0;

                    for (int px = x; px < x + size; px++)
                    {
                        if (!dashedLine || px % 10 < 6)
                        {
                            image[px, centerY] = lineColor;

                            if (centerY - 1 >= y)
                                image[px, centerY - 1] = lineColor;
                        }
                    }
                }
                else
                {
                    int centerX = x + size / 2;

                    bool dashedLine = random.Next(2) == 0;

                    for (int py = y; py < y + size; py++)
                    {
                        if (!dashedLine || py % 10 < 6)
                        {
                            image[centerX, py] = lineColor;

                            if (centerX - 1 >= x)
                                image[centerX - 1, py] = lineColor;
                        }
                    }
                }

                for (int i = 0; i < 3; i++)
                {
                    int markX = x + random.Next(size);
                    int markY = y + random.Next(size);
                    int markSize = random.Next(1, 4);

                    for (int my = markY - markSize; my <= markY + markSize; my++)
                    {
                        for (int mx = markX - markSize; mx <= markX + markSize; mx++)
                        {
                            double distance = Math.Sqrt((mx - markX) * (mx - markX) + (my - markY) * (my - markY));
                            if (distance <= markSize && random.Next(3) > 0)
                            {
                                if (mx >= x && mx < x + size && my >= y && my < y + size)
                                {
                                    image[mx, my] = edgeColor;
                                }
                            }
                        }
                    }
                }
            });
        }

        static void DrawOre(Image<Rgba32> image, int x, int y, int size)
        {
            var baseColor = new Rgba32(224, 180, 0, 255);
            var darkerColor = new Rgba32(180, 140, 0, 255);
            var lighterColor = new Rgba32(255, 215, 80, 255);
            var sparkleColor = new Rgba32(255, 255, 200, 255);

            image.Mutate(ctx =>
            {
                ctx.Fill(baseColor, new Rectangle(x, y, size, size));

                var random = new Random((x + y) * 1000);

                // Create ore chunks of different sizes
                for (int i = 0; i < 12; i++)
                {
                    int chunkX = x + random.Next(size);
                    int chunkY = y + random.Next(size);
                    int chunkSize = random.Next(3, 10);
                    bool isLight = random.Next(2) == 0;
                    var chunkColor = isLight ? lighterColor : darkerColor;

                    for (int cy = chunkY - chunkSize; cy <= chunkY + chunkSize; cy++)
                    {
                        for (int cx = chunkX - chunkSize; cx <= chunkX + chunkSize; cx++)
                        {
                            double distance = Math.Sqrt((cx - chunkX) * (cx - chunkX) + (cy - chunkY) * (cy - chunkY));
                            if (distance <= chunkSize)
                            {
                                if (cx >= x && cx < x + size && cy >= y && cy < y + size)
                                {
                                    if (random.Next(100) < 80) // Some randomness in chunk shape
                                    {
                                        // Add depth perception with lighting
                                        if (cx < chunkX && cy < chunkY)
                                        {
                                            // Lighter for top-left (highlight)
                                            image[cx, cy] = isLight ? sparkleColor : lighterColor;
                                        }
                                        else if (cx > chunkX && cy > chunkY)
                                        {
                                            // Darker for bottom-right (shadow)
                                            image[cx, cy] = darkerColor;
                                        }
                                        else
                                        {
                                            image[cx, cy] = chunkColor;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                // Add sparkles/highlights to represent valuable minerals
                for (int i = 0; i < 20; i++)
                {
                    int sparkleX = x + random.Next(size);
                    int sparkleY = y + random.Next(size);

                    // Small sparkle point
                    if (sparkleX >= x && sparkleX < x + size && sparkleY >= y && sparkleY < y + size)
                    {
                        image[sparkleX, sparkleY] = sparkleColor;

                        // Add a tiny cross pattern for the sparkle
                        if (sparkleX + 1 < x + size) image[sparkleX + 1, sparkleY] = sparkleColor;
                        if (sparkleX - 1 >= x) image[sparkleX - 1, sparkleY] = sparkleColor;
                        if (sparkleY + 1 < y + size) image[sparkleX, sparkleY + 1] = sparkleColor;
                        if (sparkleY - 1 >= y) image[sparkleX, sparkleY - 1] = sparkleColor;
                    }
                }

                // Add base texture variation
                for (int py = y; py < y + size; py++)
                {
                    for (int px = x; px < x + size; px++)
                    {
                        var currentColor = image[px, py];
                        if (ColorMatch(currentColor, baseColor))
                        {
                            if (random.Next(100) < 40)
                            {
                                int variation = random.Next(-20, 21);
                                image[px, py] = new Rgba32(
                                    (byte)Math.Clamp(baseColor.R + variation, 0, 255),
                                    (byte)Math.Clamp(baseColor.G + variation, 0, 255),
                                    (byte)Math.Clamp(baseColor.B + variation / 2, 0, 255), // Less blue variation to keep gold tone
                                    255);
                            }
                        }
                    }
                }
            });
        }

        static void DrawRidge(Image<Rgba32> image, int x, int y, int size, Rgba32 baseColor)
        {
            var shadowColor = new Rgba32(
                (byte)Math.Max(0, baseColor.R - 40),
                (byte)Math.Max(0, baseColor.G - 40),
                (byte)Math.Max(0, baseColor.B - 40),
                255);

            var highlightColor = new Rgba32(
                (byte)Math.Min(255, baseColor.R + 30),
                (byte)Math.Min(255, baseColor.G + 30),
                (byte)Math.Min(255, baseColor.B + 30),
                255);

            image.Mutate(ctx =>
            {
                ctx.Fill(baseColor, new Rectangle(x, y, size, size));

                var random = new Random((x + y) * 1000);

                // Determine ridge direction (horizontal or vertical)
                bool horizontalRidge = random.Next(2) == 0;

                if (horizontalRidge)
                {
                    // Create horizontal ridge with shadow and highlight
                    int ridgeY = y + size / 2;
                    int ridgeHeight = size / 3;
                    int ridge1 = ridgeY - ridgeHeight / 2;
                    int ridge2 = ridgeY + ridgeHeight / 2;

                    // Add some variation to the ridge line
                    int[] ridgeVariation = new int[size];
                    for (int i = 0; i < size; i++)
                    {
                        ridgeVariation[i] = random.Next(-ridgeHeight / 4, ridgeHeight / 4 + 1);
                    }

                    // Smooth the variation
                    for (int i = 1; i < size - 1; i++)
                    {
                        ridgeVariation[i] = (ridgeVariation[i - 1] + ridgeVariation[i] + ridgeVariation[i + 1]) / 3;
                    }

                    // Draw the ridge with shading
                    for (int px = x; px < x + size; px++)
                    {
                        int localVar = ridgeVariation[px - x];

                        // Upper part (highlight)
                        for (int py = y; py < ridge1 + localVar; py++)
                        {
                            double dist = (ridge1 + localVar - py) / (double)(ridge1 - y);
                            double blend = Math.Clamp(dist * 0.7, 0, 1);

                            image[px, py] = new Rgba32(
                                (byte)Math.Clamp(baseColor.R + (highlightColor.R - baseColor.R) * blend, 0, 255),
                                (byte)Math.Clamp(baseColor.G + (highlightColor.G - baseColor.G) * blend, 0, 255),
                                (byte)Math.Clamp(baseColor.B + (highlightColor.B - baseColor.B) * blend, 0, 255),
                                255);
                        }

                        // Ridge line
                        for (int py = ridge1 + localVar; py <= ridge2 + localVar; py++)
                        {
                            if (py >= y && py < y + size)
                            {
                                image[px, py] = baseColor;
                            }
                        }

                        // Lower part (shadow)
                        for (int py = ridge2 + localVar + 1; py < y + size; py++)
                        {
                            double dist = (py - ridge2 - localVar) / (double)(y + size - ridge2);
                            double blend = Math.Clamp(dist * 0.8, 0, 1);

                            image[px, py] = new Rgba32(
                                (byte)Math.Clamp(baseColor.R + (shadowColor.R - baseColor.R) * blend, 0, 255),
                                (byte)Math.Clamp(baseColor.G + (shadowColor.G - baseColor.G) * blend, 0, 255),
                                (byte)Math.Clamp(baseColor.B + (shadowColor.B - baseColor.B) * blend, 0, 255),
                                255);
                        }
                    }
                }
                else
                {
                    // Create vertical ridge with shadow and highlight
                    int ridgeX = x + size / 2;
                    int ridgeWidth = size / 3;
                    int ridge1 = ridgeX - ridgeWidth / 2;
                    int ridge2 = ridgeX + ridgeWidth / 2;

                    // Add some variation to the ridge line
                    int[] ridgeVariation = new int[size];
                    for (int i = 0; i < size; i++)
                    {
                        ridgeVariation[i] = random.Next(-ridgeWidth / 4, ridgeWidth / 4 + 1);
                    }

                    // Smooth the variation
                    for (int i = 1; i < size - 1; i++)
                    {
                        ridgeVariation[i] = (ridgeVariation[i - 1] + ridgeVariation[i] + ridgeVariation[i + 1]) / 3;
                    }

                    // Draw the ridge with shading
                    for (int py = y; py < y + size; py++)
                    {
                        int localVar = ridgeVariation[py - y];

                        // Left part (shadow)
                        for (int px = x; px < ridge1 + localVar; px++)
                        {
                            double dist = (ridge1 + localVar - px) / (double)(ridge1 - x);
                            double blend = Math.Clamp(dist * 0.8, 0, 1);

                            image[px, py] = new Rgba32(
                                (byte)Math.Clamp(baseColor.R + (shadowColor.R - baseColor.R) * blend, 0, 255),
                                (byte)Math.Clamp(baseColor.G + (shadowColor.G - baseColor.G) * blend, 0, 255),
                                (byte)Math.Clamp(baseColor.B + (shadowColor.B - baseColor.B) * blend, 0, 255),
                                255);
                        }

                        // Ridge line
                        for (int px = ridge1 + localVar; px <= ridge2 + localVar; px++)
                        {
                            if (px >= x && px < x + size)
                            {
                                image[px, py] = baseColor;
                            }
                        }

                        // Right part (highlight)
                        for (int px = ridge2 + localVar + 1; px < x + size; px++)
                        {
                            double dist = (px - ridge2 - localVar) / (double)(x + size - ridge2);
                            double blend = Math.Clamp(dist * 0.7, 0, 1);

                            image[px, py] = new Rgba32(
                                (byte)Math.Clamp(baseColor.R + (highlightColor.R - baseColor.R) * blend, 0, 255),
                                (byte)Math.Clamp(baseColor.G + (highlightColor.G - baseColor.G) * blend, 0, 255),
                                (byte)Math.Clamp(baseColor.B + (highlightColor.B - baseColor.B) * blend, 0, 255),
                                255);
                        }
                    }
                }

                // Add texture variation
                for (int py = y; py < y + size; py++)
                {
                    for (int px = x; px < x + size; px++)
                    {
                        if (random.Next(100) < 30)
                        {
                            var currentColor = image[px, py];
                            int variation = random.Next(-10, 11);
                            image[px, py] = new Rgba32(
                                (byte)Math.Clamp(currentColor.R + variation, 0, 255),
                                (byte)Math.Clamp(currentColor.G + variation, 0, 255),
                                (byte)Math.Clamp(currentColor.B + variation, 0, 255),
                                255);
                        }
                    }
                }
            });
        }

        static bool ColorMatch(Rgba32 c1, Rgba32 c2)
        {
            int threshold = 30;
            return Math.Abs(c1.R - c2.R) < threshold &&
                   Math.Abs(c1.G - c2.G) < threshold &&
                   Math.Abs(c1.B - c2.B) < threshold;
        }

        static void DrawTrees(Image<Rgba32> image, int x, int y, int size)
        {
            var groundColor = new Rgba32(76, 120, 40, 255);
            var trunkColor = new Rgba32(101, 67, 33, 255);
            var canopyColor = new Rgba32(20, 80, 20, 255);
            var canopyHighlightColor = new Rgba32(40, 100, 40, 255);
            var canopyShadowColor = new Rgba32(10, 50, 10, 255);

            image.Mutate(ctx =>
            {
                ctx.Fill(groundColor, new Rectangle(x, y, size, size));

                var random = new Random((x + y) * 1000);

                // Draw multiple trees
                int treeCount = random.Next(2, 5);
                for (int i = 0; i < treeCount; i++)
                {
                    int treeX = x + random.Next(size - 20) + 10;
                    int treeY = y + random.Next(size - 20) + 10;
                    int trunkWidth = random.Next(3, 6);
                    int trunkHeight = random.Next(10, 20);
                    int canopySize = random.Next(15, 25);

                    // Draw trunk
                    for (int tx = treeX - trunkWidth / 2; tx < treeX + trunkWidth / 2; tx++)
                    {
                        for (int ty = treeY; ty < treeY + trunkHeight; ty++)
                        {
                            if (tx >= x && tx < x + size && ty >= y && ty < y + size)
                            {
                                image[tx, ty] = trunkColor;
                            }
                        }
                    }

                    // Draw canopy (circular)
                    int canopyY = treeY - canopySize / 3;
                    for (int cy = canopyY - canopySize; cy <= canopyY + canopySize; cy++)
                    {
                        for (int cx = treeX - canopySize; cx <= treeX + canopySize; cx++)
                        {
                            double distance = Math.Sqrt((cx - treeX) * (cx - treeX) + (cy - canopyY) * (cy - canopyY));
                            if (distance <= canopySize)
                            {
                                if (cx >= x && cx < x + size && cy >= y && cy < y + size)
                                {
                                    // Base canopy color
                                    var color = canopyColor;

                                    // Add some variation
                                    if (distance < canopySize - 5 && random.Next(100) < 40)
                                    {
                                        color = canopyHighlightColor;
                                    }
                                    else if (distance > canopySize - 5 && random.Next(100) < 60)
                                    {
                                        color = canopyShadowColor;
                                    }

                                    image[cx, cy] = color;
                                }
                            }
                        }
                    }
                }

                // Add some ground variation
                for (int py = y; py < y + size; py++)
                {
                    for (int px = x; px < x + size; px++)
                    {
                        if (random.Next(100) < 30)
                        {
                            var pixelColor = image[px, py];
                            if (ColorMatch(pixelColor, groundColor))
                            {
                                int variation = random.Next(-10, 11);
                                image[px, py] = new Rgba32(
                                    (byte)Math.Clamp(groundColor.R + variation, 0, 255),
                                    (byte)Math.Clamp(groundColor.G + variation, 0, 255),
                                    (byte)Math.Clamp(groundColor.B + variation, 0, 255),
                                    255);
                            }
                        }
                    }
                }
            });
        }

        static void DrawSnowTrees(Image<Rgba32> image, int x, int y, int size)
        {
            var groundColor = new Rgba32(220, 230, 240, 255);
            var trunkColor = new Rgba32(80, 60, 40, 255);
            var canopyColor = new Rgba32(80, 130, 100, 255);
            var canopyHighlightColor = new Rgba32(100, 150, 120, 255);
            var canopyShadowColor = new Rgba32(60, 100, 80, 255);
            var snowCapColor = new Rgba32(230, 240, 250, 255);

            image.Mutate(ctx =>
            {
                ctx.Fill(groundColor, new Rectangle(x, y, size, size));

                var random = new Random((x + y) * 1000);

                // Draw multiple snow-covered trees
                int treeCount = random.Next(2, 5);
                for (int i = 0; i < treeCount; i++)
                {
                    int treeX = x + random.Next(size - 20) + 10;
                    int treeY = y + random.Next(size - 20) + 10;
                    int trunkWidth = random.Next(3, 6);
                    int trunkHeight = random.Next(8, 16);
                    int canopySize = random.Next(15, 25);

                    // Draw trunk
                    for (int tx = treeX - trunkWidth / 2; tx < treeX + trunkWidth / 2; tx++)
                    {
                        for (int ty = treeY; ty < treeY + trunkHeight; ty++)
                        {
                            if (tx >= x && tx < x + size && ty >= y && ty < y + size)
                            {
                                image[tx, ty] = trunkColor;
                            }
                        }
                    }

                    // Draw canopy (triangular for pine/fir trees)
                    int canopyY = treeY;
                    int layers = random.Next(3, 6);
                    int layerHeight = trunkHeight / (layers + 1);

                    for (int layer = 0; layer < layers; layer++)
                    {
                        int layerWidth = canopySize - (canopySize * layer / layers);
                        int layerTop = canopyY - (layer + 1) * layerHeight;

                        for (int cy = layerTop; cy < layerTop + layerHeight; cy++)
                        {
                            int rowWidth = layerWidth * (layerTop + layerHeight - cy) / layerHeight;
                            for (int cx = treeX - rowWidth; cx <= treeX + rowWidth; cx++)
                            {
                                if (cx >= x && cx < x + size && cy >= y && cy < y + size)
                                {
                                    // Base canopy color
                                    var color = canopyColor;

                                    // Add some variation
                                    double distFromCenter = Math.Abs(cx - treeX) / (double)rowWidth;
                                    if (distFromCenter < 0.3 && random.Next(100) < 40)
                                    {
                                        color = canopyHighlightColor;
                                    }
                                    else if (distFromCenter > 0.7 && random.Next(100) < 60)
                                    {
                                        color = canopyShadowColor;
                                    }

                                    // Add snow on top
                                    if (cy == layerTop || (cy == layerTop + 1 && random.Next(100) < 70))
                                    {
                                        color = snowCapColor;
                                    }

                                    image[cx, cy] = color;
                                }
                            }
                        }
                    }

                    // Add a snow cap at the top
                    int capSize = layerHeight / 2;
                    for (int cy = canopyY - layers * layerHeight - capSize; cy < canopyY - layers * layerHeight; cy++)
                    {
                        for (int cx = treeX - capSize; cx <= treeX + capSize; cx++)
                        {
                            double distance = Math.Sqrt((cx - treeX) * (cx - treeX) + (cy - (canopyY - layers * layerHeight)) * (cy - (canopyY - layers * layerHeight)));
                            if (distance <= capSize)
                            {
                                if (cx >= x && cx < x + size && cy >= y && cy < y + size)
                                {
                                    image[cx, cy] = snowCapColor;
                                }
                            }
                        }
                    }
                }

                // Add some ground variation with snow patterns
                for (int py = y; py < y + size; py++)
                {
                    for (int px = x; px < x + size; px++)
                    {
                        if (random.Next(100) < 40)
                        {
                            var pixelColor = image[px, py];
                            if (ColorMatch(pixelColor, groundColor))
                            {
                                int variation = random.Next(-5, 16);
                                image[px, py] = new Rgba32(
                                    (byte)Math.Clamp(groundColor.R + variation, 0, 255),
                                    (byte)Math.Clamp(groundColor.G + variation, 0, 255),
                                    (byte)Math.Clamp(groundColor.B + variation, 0, 255),
                                    255);
                            }
                        }
                    }
                }
            });
        }

        static void DrawBridge(Image<Rgba32> image, int x, int y, int size)
        {
            // In OpenRA, bridges are rendered over water with wooden planks and supports
            // Color definitions for bridge components
            var waterColor = new Rgba32(64, 147, 206, 255);
            var waterHighlightColor = new Rgba32(100, 183, 242, 255);
            var bridgeDeckColor = new Rgba32(140, 100, 60, 255); // Main bridge color
            var plankColor = new Rgba32(165, 120, 70, 255); // Wood planks
            var darkPlankColor = new Rgba32(120, 85, 45, 255); // Darker wood planks
            var railingColor = new Rgba32(90, 60, 30, 255); // Bridge railing
            var supportColor = new Rgba32(100, 70, 40, 255); // Vertical supports
            var shadowColor = new Rgba32(30, 30, 30, 128); // Shadow under bridge

            image.Mutate(ctx =>
            {
                // Fill the base with water
                ctx.Fill(waterColor, new Rectangle(x, y, size, size));

                var random = new Random((x + y) * 1000);

                // Add some water texture/highlights
                for (int i = 0; i < size / 3; i++)
                {
                    int wx = x + random.Next(size);
                    int wy = y + random.Next(size);
                    int waveSize = random.Next(2, 4);

                    for (int dx = -waveSize; dx <= waveSize; dx++)
                    {
                        for (int dy = -waveSize; dy <= waveSize; dy++)
                        {
                            int px = wx + dx;
                            int py = wy + dy;
                            if (px >= x && px < x + size && py >= y && py < y + size)
                            {
                                double dist = Math.Sqrt(dx * dx + dy * dy);
                                if (dist <= waveSize)
                                    image[px, py] = waterHighlightColor;
                            }
                        }
                    }
                }

                // Determine bridge direction (horizontal or vertical)
                // In a real implementation, this would check adjacent tiles
                bool horizontalBridge = true;

                // Check surrounding pixels to determine bridge direction if this is part of a larger image
                if (x > 0 && y > 0 && x + size < image.Width && y + size < image.Height)
                {
                    bool leftConnected = false;
                    bool rightConnected = false;
                    bool topConnected = false;
                    bool bottomConnected = false;

                    // Check pixels on each side to see if they're bridge colored
                    for (int i = 0; i < size; i++)
                    {
                        // Left side
                        var leftColor = image[x - 1, y + i];
                        if (ColorMatch(leftColor, bridgeDeckColor))
                            leftConnected = true;

                        // Right side
                        var rightColor = image[x + size, y + i];
                        if (ColorMatch(rightColor, bridgeDeckColor))
                            rightConnected = true;

                        // Top side
                        var topColor = image[x + i, y - 1];
                        if (ColorMatch(topColor, bridgeDeckColor))
                            topConnected = true;

                        // Bottom side
                        var bottomColor = image[x + i, y + size];
                        if (ColorMatch(bottomColor, bridgeDeckColor))
                            bottomConnected = true;
                    }

                    // If vertical connections exist but not horizontal, make a vertical bridge
                    if ((topConnected || bottomConnected) && !(leftConnected || rightConnected))
                        horizontalBridge = false;
                }

                if (horizontalBridge)
                {
                    // Draw horizontal bridge
                    int bridgeY = y + size / 3;
                    int bridgeHeight = size / 3;

                    // Bridge shadow on water
                    for (int py = bridgeY + bridgeHeight; py < bridgeY + bridgeHeight + 5; py++)
                    {
                        if (py < y + size)
                        {
                            for (int px = x; px < x + size; px++)
                            {
                                if (random.Next(10) > 2) // Add some randomness to the shadow
                                    image[px, py] = shadowColor;
                            }
                        }
                    }

                    // Bridge deck
                    for (int py = bridgeY; py < bridgeY + bridgeHeight; py++)
                    {
                        for (int px = x; px < x + size; px++)
                        {
                            image[px, py] = bridgeDeckColor;
                        }
                    }

                    // Planks (horizontal lines across bridge)
                    for (int px = x; px < x + size; px += 4)
                    {
                        for (int py = bridgeY + 2; py < bridgeY + bridgeHeight - 2; py++)
                        {
                            image[px, py] = (px / 4) % 2 == 0 ? plankColor : darkPlankColor;
                            if (px + 1 < x + size)
                                image[px + 1, py] = (px / 4) % 2 == 0 ? plankColor : darkPlankColor;
                        }
                    }

                    // Bridge railings
                    for (int px = x; px < x + size; px++)
                    {
                        image[px, bridgeY] = railingColor;
                        image[px, bridgeY + 1] = railingColor;
                        image[px, bridgeY + bridgeHeight - 1] = railingColor;
                        image[px, bridgeY + bridgeHeight - 2] = railingColor;
                    }

                    // Support pillars
                    int pillarWidth = size / 12;
                    for (int p = 0; p < 2; p++)
                    {
                        int pillarX = x + (p == 0 ? size / 4 : 3 * size / 4);

                        // Draw thicker support pillar
                        for (int px = pillarX - pillarWidth; px < pillarX + pillarWidth; px++)
                        {
                            for (int py = bridgeY + bridgeHeight; py < y + size; py++)
                            {
                                if (px >= x && px < x + size)
                                    image[px, py] = supportColor;
                            }
                        }

                        // Draw diagonal supports
                        for (int i = 0; i < bridgeHeight; i++)
                        {
                            int supportX1 = pillarX - i / 2;
                            int supportX2 = pillarX + i / 2;
                            int supportY = bridgeY + bridgeHeight - i;

                            if (supportX1 >= x && supportX1 < x + size && supportY >= y && supportY < y + size)
                                image[supportX1, supportY] = supportColor;

                            if (supportX2 >= x && supportX2 < x + size && supportY >= y && supportY < y + size)
                                image[supportX2, supportY] = supportColor;
                        }
                    }
                }
                else
                {
                    // Draw vertical bridge
                    int bridgeX = x + size / 3;
                    int bridgeWidth = size / 3;

                    // Bridge shadow on water
                    for (int px = bridgeX + bridgeWidth; px < bridgeX + bridgeWidth + 5; px++)
                    {
                        if (px < x + size)
                        {
                            for (int py = y; py < y + size; py++)
                            {
                                if (random.Next(10) > 2) // Add some randomness to the shadow
                                    image[px, py] = shadowColor;
                            }
                        }
                    }

                    // Bridge deck
                    for (int px = bridgeX; px < bridgeX + bridgeWidth; px++)
                    {
                        for (int py = y; py < y + size; py++)
                        {
                            image[px, py] = bridgeDeckColor;
                        }
                    }

                    // Planks (horizontal lines across bridge)
                    for (int py = y; py < y + size; py += 4)
                    {
                        for (int px = bridgeX + 2; px < bridgeX + bridgeWidth - 2; px++)
                        {
                            image[px, py] = (py / 4) % 2 == 0 ? plankColor : darkPlankColor;
                            if (py + 1 < y + size)
                                image[px, py + 1] = (py / 4) % 2 == 0 ? plankColor : darkPlankColor;
                        }
                    }

                    // Bridge railings
                    for (int py = y; py < y + size; py++)
                    {
                        image[bridgeX, py] = railingColor;
                        image[bridgeX + 1, py] = railingColor;
                        image[bridgeX + bridgeWidth - 1, py] = railingColor;
                        image[bridgeX + bridgeWidth - 2, py] = railingColor;
                    }

                    // Support pillars
                    int pillarHeight = size / 12;
                    for (int p = 0; p < 2; p++)
                    {
                        int pillarY = y + (p == 0 ? size / 4 : 3 * size / 4);

                        // Draw thicker support pillar
                        for (int py = pillarY - pillarHeight; py < pillarY + pillarHeight; py++)
                        {
                            for (int px = x; px < bridgeX; px++)
                            {
                                if (py >= y && py < y + size)
                                    image[px, py] = supportColor;
                            }
                        }

                        // Draw diagonal supports from left to center
                        for (int i = 0; i < bridgeWidth; i++)
                        {
                            int supportY1 = pillarY - i / 2;
                            int supportY2 = pillarY + i / 2;
                            int supportX = bridgeX - i;

                            if (supportY1 >= y && supportY1 < y + size && supportX >= x && supportX < x + size)
                                image[supportX, supportY1] = supportColor;

                            if (supportY2 >= y && supportY2 < y + size && supportX >= x && supportX < x + size)
                                image[supportX, supportY2] = supportColor;
                        }

                        // Draw support pillar on right side too
                        for (int py = pillarY - pillarHeight; py < pillarY + pillarHeight; py++)
                        {
                            for (int px = bridgeX + bridgeWidth; px < x + size; px++)
                            {
                                if (py >= y && py < y + size)
                                    image[px, py] = supportColor;
                            }
                        }

                        // Draw diagonal supports from right to center
                        for (int i = 0; i < bridgeWidth; i++)
                        {
                            int supportY1 = pillarY - i / 2;
                            int supportY2 = pillarY + i / 2;
                            int supportX = bridgeX + bridgeWidth + i - 1;

                            if (supportY1 >= y && supportY1 < y + size && supportX >= x && supportX < x + size)
                                image[supportX, supportY1] = supportColor;

                            if (supportY2 >= y && supportY2 < y + size && supportX >= x && supportX < x + size)
                                image[supportX, supportY2] = supportColor;
                        }
                    }
                }
            });
        }

        static void DrawDesertTerrain(Image<Rgba32> image, int x, int y, int size, byte terrainType, byte height, byte rampType)
        {
            switch (terrainType)
            {
                case 1:
                    DrawWater(image, x, y, size);
                    break;
                case 2:
                    DrawRoughTerrain(image, x, y, size, new Rgba32(170, 120, 70, 255));
                    break;
                case 3:
                    DrawRoad(image, x, y, size, new Rgba32(144, 122, 81, 255));
                    break;
                case 5:
                    DrawOre(image, x, y, size);
                    break;
                case 7:
                    DrawRidge(image, x, y, size, new Rgba32(205, 170, 125, 255));
                    break;
            }
        }

        static void DrawTemperateTerrain(Image<Rgba32> image, int x, int y, int size, byte terrainType, byte height, byte rampType)
        {
            switch (terrainType)
            {
                case 1:
                    DrawWater(image, x, y, size);
                    break;
                case 2:
                    DrawRoughTerrain(image, x, y, size, new Rgba32(130, 120, 100, 255));
                    break;
                case 3:
                    DrawRoad(image, x, y, size, new Rgba32(149, 132, 95, 255));
                    break;
                case 4:
                    DrawTrees(image, x, y, size);
                    break;
                case 5:
                    DrawOre(image, x, y, size);
                    break;
                case 7:
                    DrawRidge(image, x, y, size, new Rgba32(128, 99, 59, 255));
                    break;
            }
        }

        static void DrawSnowTerrain(Image<Rgba32> image, int x, int y, int size, byte terrainType, byte height, byte rampType)
        {
            switch (terrainType)
            {
                case 1:
                    DrawIce(image, x, y, size);
                    break;
                case 2:
                    DrawRoughTerrain(image, x, y, size, new Rgba32(150, 150, 160, 255));
                    break;
                case 3:
                    DrawRoad(image, x, y, size, new Rgba32(149, 152, 155, 255));
                    break;
                case 4:
                    DrawSnowTrees(image, x, y, size);
                    break;
                case 5:
                    DrawOre(image, x, y, size);
                    break;
                case 7:
                    DrawRidge(image, x, y, size, new Rgba32(128, 120, 110, 255));
                    break;
            }
        }

        static void DrawInteriorTerrain(Image<Rgba32> image, int x, int y, int size, byte terrainType, byte height, byte rampType)
        {
            switch (terrainType)
            {
                case 1:
                    DrawWater(image, x, y, size);
                    break;
                case 2:
                    DrawWall(image, x, y, size);
                    break;
                case 3:
                    DrawRoad(image, x, y, size, new Rgba32(130, 130, 130, 255));
                    break;
                case 5:
                    DrawOre(image, x, y, size);
                    break;
            }
        }

        static void DrawGenericTerrain(Image<Rgba32> image, int x, int y, int size, byte terrainType, byte height, byte rampType)
        {
            switch (terrainType)
            {
                case 1:
                    DrawWater(image, x, y, size);
                    break;
                case 2:
                    DrawRoughTerrain(image, x, y, size, new Rgba32(170, 120, 70, 255));
                    break;
                case 3:
                    DrawRoad(image, x, y, size, new Rgba32(120, 120, 120, 255));
                    break;
                case 5:
                    DrawOre(image, x, y, size);
                    break;
            }
        }

        static void DrawTile(Image<Rgba32> image, int x, int y, int size, TemplateTileExportInfo tileInfo, string tileset)
        {
            int[] baseColor = tileInfo.MinColor;
            byte terrainType = tileInfo.TerrainType;
            byte height = tileInfo.Height;
            byte rampType = tileInfo.RampType;

            var tileRect = new Rectangle(x, y, size, size);

            image.Mutate(ctx => ctx.Fill(Color.FromRgba(
                (byte)baseColor[0],
                (byte)baseColor[1],
                (byte)baseColor[2],
                (byte)baseColor[3]),
                tileRect));

            if (tileset.Equals("DESERT", StringComparison.OrdinalIgnoreCase))
                DrawDesertTerrain(image, x, y, size, terrainType, height, rampType);
            else if (tileset.Equals("TEMPERAT", StringComparison.OrdinalIgnoreCase))
                DrawTemperateTerrain(image, x, y, size, terrainType, height, rampType);
            else if (tileset.Equals("SNOW", StringComparison.OrdinalIgnoreCase))
                DrawSnowTerrain(image, x, y, size, terrainType, height, rampType);
            else if (tileset.Equals("INTERIOR", StringComparison.OrdinalIgnoreCase))
                DrawInteriorTerrain(image, x, y, size, terrainType, height, rampType);
            else
                DrawGenericTerrain(image, x, y, size, terrainType, height, rampType);

            if (height > 0)
            {
                var font = SystemFonts.CreateFont("Arial", size / 4, FontStyle.Bold);
                image.Mutate(ctx =>
                {
                    ctx.DrawText(height.ToString(), font, Color.White,
                        new PointF(x + size / 2 - size / 8, y + size / 2 - size / 8));
                });
            }

            image.Mutate(ctx => ctx.Draw(Color.Black, 1, tileRect));

            if (rampType > 0)
            {
                DrawRamp(image, x, y, size, rampType);
            }
        }

        static void Main(string[] args)
        {
            try
            {
                if (args.Length < 1 || args[0] == "--help" || args[0] == "-h")
                {
                    PrintUsage();
                    return;
                }

                var inputPath = args[0];
                var outputPath = args.Length > 1 ? args[1] : Path.Combine(Directory.GetCurrentDirectory(), "maps_parsed");

                var templatesDir = Path.Combine(outputPath, "templates");
                Directory.CreateDirectory(templatesDir);

                if (IsDirectory(inputPath))
                {
                    ProcessMapsDirectory(inputPath, outputPath);
                }
                else
                {
                    ProcessSingleMap(inputPath, outputPath);
                }

                ExtractTemplateInformation(outputPath, templatesDir);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error: {ex.Message}");
                Console.ResetColor();

                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner error: {ex.InnerException.Message}");
                }

                Console.WriteLine("Use --help for usage information");
            }
        }

        static void ExtractTemplateInformation(string mapsOutputDir, string templatesDir)
        {
            Console.WriteLine("Extracting template information from processed maps...");

            var jsonSettings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore
            };

            var jsonFiles = Directory.GetFiles(mapsOutputDir, "map.json", SearchOption.AllDirectories);

            if (jsonFiles.Length == 0)
            {
                Console.WriteLine("No map JSON files found to extract template information from.");
                return;
            }

            var tilesetTemplates = new Dictionary<string, Dictionary<ushort, TemplateExportInfo>>();

            foreach (var jsonFile in jsonFiles)
            {
                try
                {
                    var json = File.ReadAllText(jsonFile);
                    var mapData = JsonConvert.DeserializeObject<MapJsonFormat>(json);

                    if (mapData == null || string.IsNullOrEmpty(mapData.Tileset))
                        continue;

                    var tileset = mapData.Tileset;

                    if (!tilesetTemplates.ContainsKey(tileset))
                        tilesetTemplates[tileset] = new Dictionary<ushort, TemplateExportInfo>();

                    foreach (var template in mapData.Templates)
                    {
                        if (tilesetTemplates[tileset].ContainsKey(template.Key))
                            continue;

                        var templateInfo = new TemplateExportInfo
                        {
                            Id = template.Value.Id,
                            Size = template.Value.Size,
                            Tiles = template.Value.Tiles.Select(t => new TemplateTileExportInfo
                            {
                                Index = t.Index,
                                TerrainType = t.TerrainType,
                                Height = t.Height,
                                RampType = 0,
                                MinColor = GetColorForTerrainType(t.TerrainType, tileset),
                                MaxColor = GetColorForTerrainType(t.TerrainType, tileset)
                            }).ToList()
                        };

                        tilesetTemplates[tileset][template.Key] = templateInfo;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Failed to extract template info from {jsonFile}: {ex.Message}");
                }
            }

            foreach (var kvp in tilesetTemplates)
            {
                var tileset = kvp.Key;
                var templates = kvp.Value;

                Console.WriteLine($"Processing tileset '{tileset}' with {templates.Count} templates...");

                var tilesetDir = Path.Combine(templatesDir, tileset);
                Directory.CreateDirectory(tilesetDir);

                var templateIndex = new Dictionary<string, object>
                {
                    ["tileset"] = tileset,
                    ["templateCount"] = templates.Count,
                    ["templates"] = templates.Keys.OrderBy(k => k).Select(k => new { id = k, name = $"Template{k}" }).ToList()
                };

                var indexJson = JsonConvert.SerializeObject(templateIndex, jsonSettings);
                File.WriteAllText(Path.Combine(tilesetDir, "index.json"), indexJson);

                foreach (var template in templates)
                {
                    var templateId = template.Key;
                    var templateData = template.Value;

                    var templateJson = JsonConvert.SerializeObject(templateData, jsonSettings);
                    File.WriteAllText(Path.Combine(tilesetDir, $"{templateId}.json"), templateJson);

                    GenerateTemplatePng(templateData, Path.Combine(tilesetDir, $"{templateId}.png"), tileset);
                }

                Console.WriteLine($"Exported {templates.Count} templates for tileset '{tileset}'");
            }

            Console.WriteLine($"Template extraction complete. Templates saved to {templatesDir}");
        }

        static int[] GetColorForTerrainType(byte terrainType, string tileset)
        {
            if (tileset.Equals("DESERT", StringComparison.OrdinalIgnoreCase))
                return GetDesertTerrainColor(terrainType);
            else if (tileset.Equals("TEMPERAT", StringComparison.OrdinalIgnoreCase))
                return GetTemperateTerrainColor(terrainType);
            else if (tileset.Equals("SNOW", StringComparison.OrdinalIgnoreCase))
                return GetSnowTerrainColor(terrainType);
            else if (tileset.Equals("INTERIOR", StringComparison.OrdinalIgnoreCase))
                return GetInteriorTerrainColor(terrainType);
            else
                return GetGenericTerrainColor(terrainType);
        }

        static int[] GetDesertTerrainColor(byte terrainType)
        {
            switch (terrainType)
            {
                case 0:
                    return new[] { 245, 222, 178, 255 };
                case 1:
                    return new[] { 64, 147, 206, 255 };
                case 2:
                    return new[] { 170, 120, 70, 255 };
                case 3:
                    return new[] { 144, 122, 81, 255 };
                case 4:
                    return new[] { 139, 102, 43, 255 };
                case 5:
                    return new[] { 224, 180, 0, 255 };
                case 6:
                    return new[] { 238, 214, 175, 255 };
                case 7:
                    return new[] { 205, 170, 125, 255 };
                default:
                    return GenerateColorFromTerrainType(terrainType, new[] { 245, 222, 178, 255 });
            }
        }

        static int[] GetTemperateTerrainColor(byte terrainType)
        {
            switch (terrainType)
            {
                case 0:
                    return new[] { 86, 151, 50, 255 };
                case 1:
                    return new[] { 52, 107, 186, 255 };
                case 2:
                    return new[] { 130, 120, 100, 255 };
                case 3:
                    return new[] { 149, 132, 95, 255 };
                case 4:
                    return new[] { 35, 92, 35, 255 };
                case 5:
                    return new[] { 224, 180, 0, 255 };
                case 6:
                    return new[] { 204, 185, 130, 255 };
                case 7:
                    return new[] { 128, 99, 59, 255 };
                default:
                    return GenerateColorFromTerrainType(terrainType, new[] { 86, 151, 50, 255 });
            }
        }

        static int[] GetSnowTerrainColor(byte terrainType)
        {
            switch (terrainType)
            {
                case 0:
                    return new[] { 236, 240, 241, 255 };
                case 1:
                    return new[] { 168, 222, 240, 255 };
                case 2:
                    return new[] { 150, 150, 160, 255 };
                case 3:
                    return new[] { 149, 152, 155, 255 };
                case 4:
                    return new[] { 105, 145, 120, 255 };
                case 5:
                    return new[] { 224, 180, 0, 255 };
                case 6:
                    return new[] { 187, 230, 240, 255 };
                case 7:
                    return new[] { 128, 120, 110, 255 };
                default:
                    return GenerateColorFromTerrainType(terrainType, new[] { 236, 240, 241, 255 });
            }
        }

        static int[] GetInteriorTerrainColor(byte terrainType)
        {
            switch (terrainType)
            {
                case 0:
                    return new[] { 150, 150, 150, 255 };
                case 1:
                    return new[] { 64, 147, 206, 255 };
                case 2:
                    return new[] { 100, 100, 100, 255 };
                case 3:
                    return new[] { 130, 130, 130, 255 };
                case 4:
                    return new[] { 170, 170, 170, 255 };
                case 5:
                    return new[] { 224, 180, 0, 255 };
                default:
                    return GenerateColorFromTerrainType(terrainType, new[] { 150, 150, 150, 255 });
            }
        }

        static int[] GetGenericTerrainColor(byte terrainType)
        {
            switch (terrainType)
            {
                case 0:
                    return new[] { 76, 230, 0, 255 };
                case 1:
                    return new[] { 0, 160, 255, 255 };
                case 2:
                    return new[] { 170, 120, 70, 255 };
                case 3:
                    return new[] { 120, 120, 120, 255 };
                case 4:
                    return new[] { 255, 215, 0, 255 };
                case 5:
                    return new[] { 224, 180, 0, 255 };
                default:
                    return GenerateColorFromTerrainType(terrainType, new[] { 76, 230, 0, 255 });
            }
        }

        static int[] GenerateColorFromTerrainType(byte terrainType, int[] baseColor)
        {
            var hue = (terrainType * 37) % 360;
            var r = 0;
            var g = 0;
            var b = 0;

            if (hue < 60)
            {
                r = 255;
                g = (int)(255 * hue / 60.0);
            }
            else if (hue < 120)
            {
                r = (int)(255 * (120 - hue) / 60.0);
                g = 255;
            }
            else if (hue < 180)
            {
                g = 255;
                b = (int)(255 * (hue - 120) / 60.0);
            }
            else if (hue < 240)
            {
                g = (int)(255 * (240 - hue) / 60.0);
                b = 255;
            }
            else if (hue < 300)
            {
                b = 255;
                r = (int)(255 * (hue - 240) / 60.0);
            }
            else
            {
                b = (int)(255 * (360 - hue) / 60.0);
                r = 255;
            }

            var blend = 0.7f;
            return new[]
            {
                (int)(r * blend + baseColor[0] * (1 - blend)),
                (int)(g * blend + baseColor[1] * (1 - blend)),
                (int)(b * blend + baseColor[2] * (1 - blend)),
                255
            };
        }

        static void GenerateTemplatePng(TemplateExportInfo template, string outputPath, string tileset)
        {
            try
            {
                int width = template.Size.X;
                int height = template.Size.Y;

                if (width <= 0 || height <= 0)
                {
                    width = 1;
                    height = 1;
                }

                int scale = 64;
                int imageWidth = width * scale;
                int imageHeight = height * scale;

                using (var image = new Image<Rgba32>(imageWidth, imageHeight))
                {
                    image.Mutate(ctx => ctx.Fill(Color.FromRgba(50, 50, 50, 255)));

                    for (int tileY = 0; tileY < height; tileY++)
                    {
                        for (int tileX = 0; tileX < width; tileX++)
                        {
                            int tileIndex = tileY * width + tileX;
                            var tileInfo = template.Tiles.FirstOrDefault(t => t.Index == tileIndex);

                            if (tileInfo != null)
                            {
                                int pixelX = tileX * scale;
                                int pixelY = tileY * scale;

                                DrawTile(image, pixelX, pixelY, scale, tileInfo, tileset);
                            }
                        }
                    }

                    image.Save(outputPath, new PngEncoder());
                }

                Console.WriteLine($"Created template image: {outputPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to generate PNG for template {template.Id}: {ex.Message}");
            }
        }

        static void ProcessMapsDirectory(string inputDirectory, string outputDirectory)
        {
            Console.WriteLine($"Scanning directory: {inputDirectory}");

            var mapCount = 0;
            var processedCount = 0;

            var mapDirectories = Directory.GetDirectories(inputDirectory, "*", SearchOption.AllDirectories);
            var mapZipFiles = Directory.GetFiles(inputDirectory, "*.zip", SearchOption.AllDirectories);

            var validMapDirectories = new List<string>();

            foreach (var dir in mapDirectories)
            {
                if (File.Exists(Path.Combine(dir, "map.yaml")) && File.Exists(Path.Combine(dir, "map.bin")))
                    validMapDirectories.Add(dir);
            }

            mapCount = validMapDirectories.Count + mapZipFiles.Length;
            Console.WriteLine($"Found {mapCount} maps to process");

            var mapReader = new MapReader();
            var jsonSettings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore
            };

            foreach (var mapDir in validMapDirectories)
            {
                try
                {
                    processedCount++;
                    Console.WriteLine($"[{processedCount}/{mapCount}] Processing map directory: {mapDir}");

                    var relativePath = Path.GetRelativePath(inputDirectory, mapDir);
                    var mapOutputDir = Path.Combine(outputDirectory, relativePath);

                    Directory.CreateDirectory(mapOutputDir);

                    var mapData = mapReader.ReadMap(mapDir);
                    var json = JsonConvert.SerializeObject(mapData, jsonSettings);

                    var jsonPath = Path.Combine(mapOutputDir, "map.json");
                    File.WriteAllText(jsonPath, json);

                    Console.WriteLine($"Map data written to: {jsonPath}");
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"Warning: Failed to process map {mapDir}: {ex.Message}");
                    Console.ResetColor();
                }
            }

            foreach (var zipFile in mapZipFiles)
            {
                try
                {
                    processedCount++;
                    Console.WriteLine($"[{processedCount}/{mapCount}] Processing map archive: {zipFile}");

                    var relativePath = Path.GetRelativePath(inputDirectory, Path.GetDirectoryName(zipFile));
                    var zipFileName = Path.GetFileNameWithoutExtension(zipFile);
                    var mapOutputDir = Path.Combine(outputDirectory, relativePath, zipFileName);

                    Directory.CreateDirectory(mapOutputDir);

                    var mapData = mapReader.ReadMap(zipFile);
                    var json = JsonConvert.SerializeObject(mapData, jsonSettings);

                    var jsonPath = Path.Combine(mapOutputDir, "map.json");
                    File.WriteAllText(jsonPath, json);

                    Console.WriteLine($"Map data written to: {jsonPath}");
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"Warning: Failed to process map {zipFile}: {ex.Message}");
                    Console.ResetColor();
                }
            }

            Console.WriteLine($"Map processing complete. Successfully processed {processedCount} maps.");
            Console.WriteLine($"Output directory: {outputDirectory}");
        }

        static void ProcessSingleMap(string mapPath, string outputPath)
        {
            Console.WriteLine($"Reading map from: {mapPath}");

            var mapReader = new MapReader();
            var mapData = mapReader.ReadMap(mapPath);

            string jsonPath;
            if (Directory.Exists(outputPath))
            {
                var mapName = Path.GetFileNameWithoutExtension(mapPath);
                if (IsDirectory(mapPath))
                    mapName = Path.GetFileName(mapPath);

                jsonPath = Path.Combine(outputPath, mapName + ".json");
            }
            else
            {
                jsonPath = outputPath;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(jsonPath));

            var jsonSettings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore
            };

            var json = JsonConvert.SerializeObject(mapData, jsonSettings);

            File.WriteAllText(jsonPath, json);

            Console.WriteLine($"Map data successfully written to: {jsonPath}");
        }

        static bool IsDirectory(string path)
        {
            try
            {
                return Directory.Exists(path);
            }
            catch
            {
                return false;
            }
        }

        static void PrintUsage()
        {
            Console.WriteLine("OpenRA.MapReader - Extracts OpenRA map data to JSON format");
            Console.WriteLine();
            Console.WriteLine("Usage: OpenRA.MapReader <input-path> [output-path]");
            Console.WriteLine();
            Console.WriteLine("  input-path   Path to an OpenRA map folder, .zip file, or directory containing multiple maps");
            Console.WriteLine("  output-path  Optional path for the output JSON file or directory (default: ./maps_parsed)");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  OpenRA.MapReader path/to/map output/map.json       # Process a single map");
            Console.WriteLine("  OpenRA.MapReader path/to/map.zip output/parsed/    # Process a single zipped map");
            Console.WriteLine("  OpenRA.MapReader path/to/maps/ output/maps_parsed/ # Process all maps in directory");
            Console.WriteLine();
            Console.WriteLine("When processing a directory, the original directory structure will be preserved in the output.");
            Console.WriteLine("The JSON output includes information about tileset templates and the Z-order");
            Console.WriteLine("of tiles for overlapping cells, which is useful for rendering the map correctly.");
            Console.WriteLine();
            Console.WriteLine("In addition to map data, tileset templates will be exported to a 'templates' subdirectory");
            Console.WriteLine("organized by tileset with individual JSON files for each template and visualization images.");
        }
    }
}
