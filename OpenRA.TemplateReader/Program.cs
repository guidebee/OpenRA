using System;
using System.CommandLine;
using System.IO;
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
				var modLoader = new ModLoader();
				var modData = modLoader.LoadModData(modId, gamePath);

				var terrain = (DefaultTerrain)modData.DefaultTerrainInfo[tileset];
				if (!terrain.Templates.TryGetValue(templateId, out var templateInfo))
				{
					Console.ForegroundColor = ConsoleColor.Red;
					Console.WriteLine($"Template {templateId} not found in tileset '{tileset}'");
					Console.ResetColor();
					return;
				}

				var converter = new TemplateConverter(modData, tileset);
				var image = converter.ConvertTemplateToImage(templateInfo);

				var outputPath = Path.Combine(Environment.CurrentDirectory, outputFolder);
				Directory.CreateDirectory(outputPath);

				var imageFilePath = Path.Combine(outputPath, $"{tileset}_template_{templateId}.png");
				using var fileStream = File.Create(imageFilePath);
				image.Save(fileStream, new PngEncoder());

				Console.WriteLine($"Template image exported to: {imageFilePath}");
			}
			catch (Exception ex)
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine($"Error exporting template: {ex.Message}");
				Console.WriteLine(ex.StackTrace);
				Console.ResetColor();
			}
		}
	}
}
