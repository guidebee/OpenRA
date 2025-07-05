using System;
using System.IO;
using System.CommandLine;
using System.Threading.Tasks;

namespace OpenRA.TemplateReader
{
	class TestProgram
	{
		static void Main(string[] args)
		{
			try
			{
				Console.WriteLine("Testing MinimalModData...");
				var gamePath = "C:\\Workspace\\OpenRA";
				var tileset = "temperat";
				
				Console.WriteLine($"Loading tileset from {gamePath}\\mods\\ra\\tilesets\\{tileset}.yaml");
				var simpleModData = new MinimalModData(tileset, gamePath);
				
				Console.WriteLine($"Loaded {simpleModData.Templates.Count} templates.");
				
				// Print first 10 templates
				int count = 0;
				foreach (var template in simpleModData.Templates)
				{
					Console.WriteLine($"Template {template.Key}: Size {template.Value.Size.X}x{template.Value.Size.Y}, Tiles: {template.Value.TilesCount}");
					if (++count >= 10)
						break;
				}
				
				Console.WriteLine("Test completed successfully!");
			}
			catch (Exception ex)
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine($"Error: {ex.Message}");
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
