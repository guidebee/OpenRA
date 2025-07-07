#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenRA.FileFormats;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Terrain;
using OpenRA.Primitives;

namespace OpenRA.Mods.Common.UtilityCommands
{
	public sealed class ExportTilesetTemplates : IUtilityCommand
	{
		string IUtilityCommand.Name => "--export-tileset-templates";

		bool IUtilityCommand.ValidateArguments(string[] args)
		{
			return args.Length >= 2;
		}

		[Desc("TILESET [--images] [--output PATH]", "Export tileset template info and optionally their associated images.")]
		void IUtilityCommand.Run(Utility utility, string[] args)
		{
			// HACK: The engine code assumes that Game.modData is set.
			var modData = Game.ModData = utility.ModData;

			// Get the tileset ID from the first argument
			var tilesetId = args[1];
			var exportImages = args.Contains("--images");

			// Get output path
			var outputPath = ".";
			for (var i = 0; i < args.Length - 1; i++)
			{
				if (args[i] == "--output")
				{
					outputPath = args[i + 1];
					break;
				}
			}

			// Make sure the output directory exists
			Directory.CreateDirectory(outputPath);

			// Create subdirectories for organizing the output
			var templatesPath = Path.Combine(outputPath, "templates");
			var imagesPath = Path.Combine(outputPath, "images");

			Directory.CreateDirectory(templatesPath);
			if (exportImages)
				Directory.CreateDirectory(imagesPath);

			// Try to get the specified tileset
			if (!modData.DefaultTerrainInfo.TryGetValue(tilesetId, out var terrainInfo))
			{
				Console.WriteLine($"Error: Tileset '{tilesetId}' not found. Available tilesets:");
				foreach (var availableTileset in modData.DefaultTerrainInfo.Keys)
					Console.WriteLine($"  {availableTileset}");
				return;
			}

			if (terrainInfo is not ITemplatedTerrainInfo templatedTerrainInfo)
			{
				Console.WriteLine($"Error: Tileset '{tilesetId}' is not a templated tileset.");
				return;
			}

			// Export tileset summary info
			ExportTilesetInfo(tilesetId, templatedTerrainInfo, outputPath);

			// Export each template
			var templates = templatedTerrainInfo.Templates;
			Console.WriteLine($"Found {templates.Count} templates in tileset '{tilesetId}'");

			foreach (var template in templates)
			{
				ExportTemplateInfo(template.Key, template.Value, templatesPath);

				if (exportImages)
					ExportTemplateImages(template.Key, template.Value, imagesPath, tilesetId, modData);
			}

			Console.WriteLine($"Export completed to {outputPath}");
		}

		private void ExportTilesetInfo(string tilesetId, ITemplatedTerrainInfo terrainInfo, string outputPath)
		{
			var filePath = Path.Combine(outputPath, "tileset_info.json");

			var terrainTypes = terrainInfo.TerrainTypes.Select(t => new
			{
				Type = t.Type,
				TargetTypes = t.TargetTypes.ToArray(),
				Color = t.Color.ToString(),
				AcceptsSmudgeType = t.AcceptsSmudgeType.ToArray()
			});

			var tilesetInfo = new
			{
				Id = tilesetId,
				TerrainTypes = terrainTypes,
				TemplateCount = terrainInfo.Templates.Count,
				EditorTemplateOrder = terrainInfo.EditorTemplateOrder
			};

			var json = Newtonsoft.Json.JsonConvert.SerializeObject(tilesetInfo, Newtonsoft.Json.Formatting.Indented);
			File.WriteAllText(filePath, json);

			Console.WriteLine($"Exported tileset info to {filePath}");
		}

		private void ExportTemplateInfo(ushort id, TerrainTemplateInfo template, string outputPath)
		{
			var filePath = Path.Combine(outputPath, $"template_{id}.json");

			var tileInfos = new List<object>();
			for (var i = 0; i < template.TilesCount; i++)
			{
				if (template[i] != null)
				{
					var tileInfo = template[i];
					tileInfos.Add(new
					{
						Index = i,
						TerrainType = tileInfo.TerrainType,
						Height = tileInfo.Height,
						RampType = tileInfo.RampType,
						MinColor = tileInfo.MinColor.ToString(),
						MaxColor = tileInfo.MaxColor.ToString()
					});
				}
			}

			var templateData = new
			{
				Id = id,
				Size = $"{template.Size.X}x{template.Size.Y}",
				PickAny = template.PickAny,
				Categories = template.Categories,
				TilesCount = template.TilesCount,
				Tiles = tileInfos
			};

			var json = Newtonsoft.Json.JsonConvert.SerializeObject(templateData, Newtonsoft.Json.Formatting.Indented);
			File.WriteAllText(filePath, json);
		}

		private void ExportTemplateImages(ushort id, TerrainTemplateInfo template, string outputPath, string tilesetId, ModData modData)
		{
			if (template is not DefaultTerrainTemplateInfo defaultTemplate || defaultTemplate.Images == null || defaultTemplate.Images.Length == 0)
			{
				GeneratePlaceholderImage(id, template, outputPath);
				return;
			}

			try
			{
				var sequences = new SequenceSet(modData.DefaultFileSystem, modData, tilesetId, null);
				
				// Get the terrain info and its palette name
				var terrainInfo = modData.DefaultTerrainInfo[tilesetId] as DefaultTerrain;
				var paletteName = TileSet.TerrainPaletteInternalName;
				
				// If the template specifies its own palette, use that instead
				if (defaultTemplate.Palette != null)
					paletteName = defaultTemplate.Palette;
					
				// Get the proper palette colors
				var palColors = GetPaletteColors(modData, paletteName);
				
				Console.WriteLine($"Using palette '{paletteName}' for template {id}");

				// Export the template images
				for (var i = 0; i < defaultTemplate.Images.Length; i++)
				{
					var imageName = defaultTemplate.Images[i];
					var outputFile = Path.Combine(outputPath, $"template_{id}_image_{i}_{imageName}.png");

					// Try to find and export the sprite frames for this template image
					try
					{
						var frames = sequences.SpriteCache.LoadFramesUncached(imageName);
						if (frames != null && frames.Length > 0)
						{
							// Export each frame as a separate PNG
							for (var f = 0; f < frames.Length; f++)
							{
								var frame = frames[f];
								var frameFile = Path.Combine(outputPath, $"template_{id}_image_{i}_{imageName}_frame_{f}.png");

								// Create and save the PNG
								var png = new Png(frame.Data, frame.Type, frame.Size.Width, frame.Size.Height, palColors);
								png.Save(frameFile);

								Console.WriteLine($"Exported frame {f} of template {id} image {imageName} to {frameFile}");
							}
						}
						else
						{
							Console.WriteLine($"Warning: Could not load frames for image {imageName} in template {id}");
						}
					}
					catch (Exception ex)
					{
						Console.WriteLine($"Error exporting image {imageName} for template {id}: {ex.Message}");
					}
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error exporting images for template {id}: {ex.Message}");
				GeneratePlaceholderImage(id, template, outputPath);
			}
		}

		private void GeneratePlaceholderImage(ushort id, TerrainTemplateInfo template, string outputPath)
		{
			var outputFile = Path.Combine(outputPath, $"template_{id}_placeholder.png");

			// Create a simple placeholder image for the template
			var width = template.Size.X * 24;  // 24 pixels per tile
			var height = template.Size.Y * 24;

			if (width <= 0) width = 24;
			if (height <= 0) height = 24;

			var data = new byte[width * height];

			// Fill with a checkerboard pattern
			for (var y = 0; y < height; y++)
			{
				for (var x = 0; x < width; x++)
				{
					var tileX = x / 24;
					var tileY = y / 24;
					var index = y * width + x;

					// Get tile info if available
					TerrainTileInfo tileInfo = null;
					var tileIndex = tileY * template.Size.X + tileX;
					if (tileIndex < template.TilesCount && template.Contains(tileIndex))
						tileInfo = template[tileIndex];

					if (tileInfo != null)
					{
						// Color based on terrain type (making sure to keep within palette range)
						data[index] = (byte)((tileInfo.TerrainType * 20 + tileInfo.Height * 4) % 255);
					}
					else
					{
						// Checkerboard pattern for tiles without info
						data[index] = (byte)(((tileX + tileY) % 2 == 0) ? 200 : 100);
					}
				}
			}

			// Create a grayscale palette
			var palColors = new Color[Palette.Size];
			for (var i = 0; i < Palette.Size; i++)
				palColors[i] = Color.FromArgb(255, i, i, i);

			var png = new Png(data, SpriteFrameType.Indexed8, width, height, palColors);
			png.Save(outputFile);

			Console.WriteLine($"Generated placeholder image for template {id} at {outputFile}");
		}
	}
}
