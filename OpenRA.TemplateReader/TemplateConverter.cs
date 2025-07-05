using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Terrain;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Point = SixLabors.ImageSharp.Point;

namespace OpenRA.TemplateReader
{
	/// <summary>
	/// Represents a simplified version of TerrainTemplateInfo for use in the template reader.
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
		readonly DefaultTerrain terrain;
		readonly DefaultTileCache tileCache;

		public TemplateConverter(ModData modData, string tileset)
		{
			terrain = (DefaultTerrain)modData.DefaultTerrainInfo[tileset];
			tileCache = new DefaultTileCache(terrain, (id, pal) => { });
		}

		// Method to handle byte[] input (template data)
		public Image<Rgba32> ConvertTemplateToImage(byte[] templateData)
		{
			// Create a dummy TerrainTemplateInfo with default values
			var templateInfo = new TemplateInfo(1, "dummy", 1, 1);

			// Just return a dummy image for now - this can be enhanced later
			return new Image<Rgba32>(24, 24);
		}

		// Method to handle TemplateInfo input with image loading callback
		public Image<Rgba32> ConvertTemplateToImage(TemplateInfo templateInfo, Func<string, byte[]> imageLoader)
		{
			// Create a dummy image for now
			return new Image<Rgba32>(templateInfo.Size.X * 24, templateInfo.Size.Y * 24);
		}

		public Image<Rgba32> ConvertTemplateToImage(TerrainTemplateInfo templateInfo)
		{
			var image = new Image<Rgba32>(templateInfo.Size.X * 24, templateInfo.Size.Y * 24);

			var i = 0;
			for (var y = 0; y < templateInfo.Size.Y; y++)
			{
				for (var x = 0; x < templateInfo.Size.X; x++)
				{
					var tile = new TerrainTile(templateInfo.Id, (byte)i++);
					var sprite = tileCache.TileSprite(tile);

					if (sprite != null)
					{
						using (var spriteImage = SpriteToImage(sprite))
						{
							if (spriteImage != null)
							{
								var drawX = x * 24;
								var drawY = y * 24;
								image.Mutate(ctx => ctx.DrawImage(spriteImage, new Point(drawX, drawY), 1f));
							}
						}
					}
				}
			}

			return image;
		}

		static Image<Rgba32> SpriteToImage(Sprite sprite)
		{
			var data = GetSpriteData(sprite);
			if (data == null)
				return null;

			return Image.LoadPixelData<Rgba32>(MemoryMarshal.Cast<byte, Rgba32>(data), (int)sprite.Size.X, (int)sprite.Size.Y);
		}

		static byte[] GetSpriteData(Sprite sprite)
		{
			if (sprite == null || sprite.Sheet == null)
				return null;

			var sheet = sprite.Sheet;
			var sheetData = sheet.GetData();
			if (sheetData == null)
				return null;

			var bounds = sprite.Bounds;
			var spriteData = new byte[bounds.Width * bounds.Height * 4];

			for (var sy = 0; sy < bounds.Height; sy++)
			{
				var sourceOffset = ((bounds.Top + sy) * sheet.Size.Width + bounds.Left) * 4;
				var destOffset = sy * bounds.Width * 4;
				Buffer.BlockCopy(sheetData, sourceOffset, spriteData, destOffset, bounds.Width * 4);
			}

			return spriteData;
		}
	}
}
