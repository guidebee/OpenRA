using System;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OpenRA.Mods.Common.Terrain;
using SixLabors.ImageSharp.Formats.Png;

namespace OpenRA.TemplateReader
{
	class Program
	{
		static async Task<int> Main(string[] args)
		{
			var rootCommand = new RootCommand("OpenRA Template Reader - Extracts and exports single template data from game files");

			var outputOption = new Option<string>(
				"--output",
				() => "template-export",
				"Output directory name for exported template data");

			var gamePathOption = new Option<string>(
				"--game-path",
				() => ".",
				"Path to the OpenRA directory");

			var tilesetOption = new Option<string>(
				"--tileset",
				"The tileset name (e.g., 'desert', 'temperat', 'snow')");

			var templateIdOption = new Option<ushort>(
				"--template-id",
				"The template ID to export (e.g., 401)");

			var modIdOption = new Option<string>(
				"--mod",
				() => "ra",
				"The mod ID to use (e.g., 'ra', 'cnc', 'd2k')");

			rootCommand.AddOption(outputOption);
			rootCommand.AddOption(gamePathOption);
			rootCommand.AddOption(tilesetOption);
			rootCommand.AddOption(templateIdOption);
			rootCommand.AddOption(modIdOption);

			tilesetOption.IsRequired = true;
			templateIdOption.IsRequired = true;

			rootCommand.SetHandler(
				(output, gamePath, tileset, templateId, modId) => ExportTemplate(output, gamePath, tileset, templateId, modId),
				outputOption,
				gamePathOption,
				tilesetOption,
				templateIdOption,
				modIdOption);

			return await rootCommand.InvokeAsync(args);
		}

		static void ExportTemplate(string outputFolder, string gamePath, string tileset, ushort templateId, string modId)
		{
			try
			{
				Console.WriteLine($"Loading mod data from {gamePath}...");
				
				// Create output directory
				var outputPath = Path.Combine(Environment.CurrentDirectory, outputFolder);
				Directory.CreateDirectory(outputPath);
				var imageFilePath = Path.Combine(outputPath, $"{tileset}_template_{templateId}.png");
				
				// First approach: Use MinimalModData to directly parse the tileset file
				Console.WriteLine("Attempting to load terrain directly...");
				try
				{
					var simpleModData = new MinimalModData(tileset, gamePath);
					
					if (!simpleModData.Templates.TryGetValue(templateId, out var simpleTemplateInfo))
					{
						Console.ForegroundColor = ConsoleColor.Red;
						Console.WriteLine($"Template {templateId} not found in tileset '{tileset}'");
						var availableTemplates = string.Join(", ", simpleModData.Templates.Keys.Take(10));
						Console.WriteLine($"Available templates: {availableTemplates}...");
						Console.ResetColor();
						return;
					}
					
					Console.WriteLine($"Found template ID {templateId}, creating info file...");
					
					// Create a simple text representation since we can't use the sprite loader
					Console.WriteLine($"Saving template info to {imageFilePath}.txt");
					
					File.WriteAllText(
						imageFilePath + ".txt",
						$"Template ID: {templateId}\n" +
						$"Size: {simpleTemplateInfo.Size.X}x{simpleTemplateInfo.Size.Y}\n" +
						$"Tiles: {simpleTemplateInfo.TilesCount}");
						
					Console.ForegroundColor = ConsoleColor.Green;
					Console.WriteLine($"Template info exported to: {imageFilePath}.txt");
					Console.ResetColor();
					return;
				}
				catch (Exception ex)
				{
					Console.WriteLine($"Error in minimal approach: {ex.Message}");
					Console.WriteLine(ex.StackTrace);
					if (ex.InnerException != null)
					{
						Console.WriteLine("Inner exception:");
						Console.WriteLine(ex.InnerException.Message);
						Console.WriteLine(ex.InnerException.StackTrace);
					}
					
					Console.WriteLine("Falling back to standard mod loading...");
				}
				
				// Second approach: Use ModData through ModLoader (this might not work due to OpenRA dependencies)
				try
				{
					var modLoader = new ModLoader();
					var modData = modLoader.LoadModData(modId, gamePath);
					Console.WriteLine("Mod data loaded successfully.");

					Console.WriteLine($"Looking for terrain {tileset}...");
					if (!modData.DefaultTerrainInfo.TryGetValue(tileset, out var terrainInfo))
					{
						Console.ForegroundColor = ConsoleColor.Red;
						Console.WriteLine($"Tileset '{tileset}' not found. Available tilesets: {string.Join(", ", modData.DefaultTerrainInfo.Keys)}");
						Console.ResetColor();
						return;
					}

					Console.WriteLine($"Found terrain: {tileset}");
					var terrain = (DefaultTerrain)terrainInfo;

					Console.WriteLine($"Looking for template ID {templateId}...");
					if (!terrain.Templates.TryGetValue(templateId, out var templateInfo))
					{
						Console.ForegroundColor = ConsoleColor.Red;
						Console.WriteLine($"Template {templateId} not found in tileset '{tileset}'");
						Console.WriteLine($"Available templates: {string.Join(", ", terrain.Templates.Keys.Take(10))}...");
						Console.ResetColor();
						return;
					}

					Console.WriteLine($"Found template ID {templateId}, converting to image...");
					var converter = new TemplateConverter(modData, tileset);
					var image = converter.ConvertTemplateToImage(templateInfo);

					Console.WriteLine($"Saving image to {imageFilePath}...");
					using var fileStream = File.Create(imageFilePath);
					image.Save(fileStream, new PngEncoder());

					Console.ForegroundColor = ConsoleColor.Green;
					Console.WriteLine($"Template image exported to: {imageFilePath}");
					Console.ResetColor();
				}
				catch (Exception ex)
				{
					Console.ForegroundColor = ConsoleColor.Red;
					Console.WriteLine($"Error in standard approach: {ex.Message}");
					Console.WriteLine(ex.StackTrace);
					if (ex.InnerException != null)
					{
						Console.WriteLine("Inner exception:");
						Console.WriteLine(ex.InnerException.Message);
						Console.WriteLine(ex.InnerException.StackTrace);
					}
					Console.ResetColor();
				}
			}
			catch (Exception ex)
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine($"Unexpected error: {ex.Message}");
				Console.WriteLine(ex.StackTrace);
				if (ex.InnerException != null)
				{
					Console.WriteLine("Inner exception:");
					Console.WriteLine(ex.InnerException.Message);
					Console.WriteLine(ex.InnerException.StackTrace);
				}
				Console.ResetColor();
			}
		}
	}
}
