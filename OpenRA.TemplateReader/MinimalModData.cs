using System;
using System.IO;
using System.Collections.Generic;
using OpenRA.Primitives;

namespace OpenRA.TemplateReader
{
	// A simplified class to extract template information
	public class MinimalModData
	{
		public class SimpleTemplateInfo
		{
			public ushort Id { get; set; }
			public int2 Size { get; set; }
			public int TilesCount { get; set; }
			public string[] Images { get; set; }
		}

		public readonly Dictionary<ushort, SimpleTemplateInfo> Templates = new();

		public MinimalModData(string tileset, string gamePath)
		{
			var tilesetPath = Path.Combine(gamePath, "mods", "ra", "tilesets", $"{tileset}.yaml");

			if (!File.Exists(tilesetPath))
				throw new FileNotFoundException($"Tileset file not found: {tilesetPath}");

			Console.WriteLine($"Loading tileset from {tilesetPath}");
			var yaml = MiniYaml.FromFile(tilesetPath);

			// Find the Templates node
			MiniYaml templatesNode = null;
			foreach (var node in yaml)
			{
				Console.WriteLine($"Found node with key: {node.Key}");
				if (node.Key == "Templates:")
				{
					templatesNode = node.Value;
					break;
				}
			}

			if (templatesNode == null)
				throw new InvalidOperationException("Templates section not found in tileset file");

			Console.WriteLine("Found Templates section, parsing templates...");

			// Parse the template data
			foreach (var template in templatesNode.Nodes)
			{
				// Extract the ID from the key format "Template@XXX:"
				string idStr = template.Key.Replace("Template@", "").Replace(":", "");
				if (!ushort.TryParse(idStr, out var id))
				{
					Console.WriteLine($"Skipping template with invalid ID: {template.Key}");
					continue;
				}

				var templateInfo = new SimpleTemplateInfo { Id = id };

				foreach (var property in template.Value.Nodes)
				{
					if (property.Key == "Size")
					{
						var parts = property.Value.Value.Split(',');
						if (parts.Length == 2 &&
							int.TryParse(parts[0], out var width) &&
							int.TryParse(parts[1], out var height))
						{
							templateInfo.Size = new int2(width, height);
						}
					}
					else if (property.Key == "Tiles")
					{
						templateInfo.TilesCount = property.Value.Nodes.Length;
					}
					else if (property.Key == "Images")
					{
						templateInfo.Images = new[] { property.Value.Value };
					}
				}

				Templates[id] = templateInfo;
			}

			Console.WriteLine($"Loaded {Templates.Count} templates from tileset {tileset}");
		}
	}
}
