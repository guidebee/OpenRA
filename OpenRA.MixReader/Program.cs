// Example: Extract all resources from a .mix file using OpenRA's MixFile

using System;
using System.IO;
using System.Linq;
using OpenRA.Mods.Cnc.FileFormats;
using OpenRA.Mods.Cnc.FileSystem;

namespace OpenRA.MixReader
{
	static class MixExtractor
	{
		public static void ExtractMix(string mixFilePath, string outputDir, string globalDbPath = null)
		{
			if (!File.Exists(mixFilePath))
			{
				Console.WriteLine($"File not found: {mixFilePath}");
				return;
			}

			Directory.CreateDirectory(outputDir);

			var globalFilenames = Array.Empty<string>();
			if (!string.IsNullOrEmpty(globalDbPath) && File.Exists(globalDbPath))
			{
				try
				{
					using (var stream = File.OpenRead(globalDbPath))
					using (var db = new XccGlobalDatabase(stream))
					{
						globalFilenames = db.Entries.ToArray();
						Console.WriteLine($"Loaded {globalFilenames.Length} filenames from global mix database");
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine($"Error loading global mix database: {ex.Message}");
				}
			}
			else
			{
				Console.WriteLine("Warning: No global mix database specified. File identification may be limited.");
			}

			try
			{
				// Create subfolder based on mix filename
				var mixName = Path.GetFileNameWithoutExtension(mixFilePath);
				var mixOutputDir = Path.Combine(outputDir, mixName);
				Directory.CreateDirectory(mixOutputDir);

				using (var fs = File.OpenRead(mixFilePath))
				{
					Console.WriteLine($"Opening mix file: {mixFilePath}");
					var mix = new MixLoader.MixFile(fs, Path.GetFileName(mixFilePath), globalFilenames);
					Console.WriteLine($"Found {mix.Contents.Count()} files in the mix");

					var extractedCount = 0;
					foreach (var filename in mix.Contents)
					{
						var outPath = Path.Combine(mixOutputDir, filename);
						var directory = Path.GetDirectoryName(outPath);
						if (!string.IsNullOrEmpty(directory))
							Directory.CreateDirectory(directory);

						using (var outStream = File.Create(outPath))
						using (var inStream = mix.GetStream(filename))
						{
							inStream.CopyTo(outStream);
						}

						Console.WriteLine($"Extracted: {filename}");
						extractedCount++;
					}

					Console.WriteLine($"Extraction complete: {extractedCount} files extracted to {mixOutputDir}");
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error extracting mix file: {ex.Message}");
				Console.WriteLine(ex.StackTrace);
			}
		}

		public static void ExtractAllMixesInDirectory(string directory, string outputDir, string globalDbPath = null)
		{
			if (!Directory.Exists(directory))
			{
				Console.WriteLine($"Directory not found: {directory}");
				return;
			}

			var mixFiles = Directory.GetFiles(directory, "*.mix", SearchOption.AllDirectories);
			Console.WriteLine($"Found {mixFiles.Length} .mix files in {directory}");

			foreach (var mixFile in mixFiles)
			{
				Console.WriteLine($"\nProcessing: {mixFile}");
				ExtractMix(mixFile, outputDir, globalDbPath);
			}

			Console.WriteLine($"\nAll mix files processed. Extracted files can be found in {outputDir}");
		}

		static void Main(string[] args)
		{
			// Initialize OpenRA logging system
			Log.AddChannel("perf", "perf.log");
			Log.AddChannel("debug", "debug.log");
			Log.AddChannel("server", "server.log");
			Log.AddChannel("sound", "sound.log");
			Log.AddChannel("graphics", "graphics.log");
			Log.AddChannel("geoip", "geoip.log");
			Log.AddChannel("filesystem", "filesystem.log");
			Log.AddChannel("platform", "platform.log");

			if (args.Length < 1)
			{
				Console.WriteLine("Usage:");
				Console.WriteLine("  extract_mix <input.mix> <output_dir> [global_mix_database.dat]");
				Console.WriteLine("  extract_mix --all <directory> <output_dir> [global_mix_database.dat]");
				return;
			}

			if (args[0] == "--all" && args.Length >= 3)
			{
				var globalDbPath = args.Length > 3 ? args[3] : "global mix database.dat";
				ExtractAllMixesInDirectory(args[1], args[2], globalDbPath);
			}
			else if (args.Length >= 2)
			{
				var globalDbPath = args.Length > 2 ? args[2] : "global mix database.dat";
				ExtractMix(args[0], args[1], globalDbPath);
			}
			else
			{
				Console.WriteLine("Invalid arguments.");
				Console.WriteLine("Usage:");
				Console.WriteLine("  extract_mix <input.mix> <output_dir> [global_mix_database.dat]");
				Console.WriteLine("  extract_mix --all <directory> <output_dir> [global_mix_database.dat]");
			}
		}
	}
}
