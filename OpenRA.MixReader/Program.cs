// Example: Extract all resources from a .mix file using OpenRA's MixFile

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenRA.Mods.Cnc.FileFormats;
using OpenRA.Mods.Cnc.FileSystem;

namespace OpenRA.MixReader
{
	static class MixExtractor
	{
		static readonly HashSet<string> ProcessedMixFiles = new(StringComparer.OrdinalIgnoreCase);

		public static void ExtractMixRecursively(
			string mixFilePath,
			string outputDir,
			string globalDbPath = null,
			int recursionLevel = 0,
			string[] globalFilenames = null)
		{
			if (recursionLevel > 10)
			{
				Console.WriteLine($"Maximum recursion level reached for: {mixFilePath}. Stopping recursion to prevent infinite loops.");
				return;
			}

			var normalizedPath = Path.GetFullPath(mixFilePath);
			if (ProcessedMixFiles.Contains(normalizedPath))
			{
				Console.WriteLine($"Already processed: {mixFilePath}. Skipping...");
				return;
			}

			ProcessedMixFiles.Add(normalizedPath);

			if (!File.Exists(mixFilePath))
			{
				Console.WriteLine($"File not found: {mixFilePath}");
				return;
			}

			Directory.CreateDirectory(outputDir);

			globalFilenames ??= LoadGlobalFilenames(globalDbPath, recursionLevel);

			try
			{
				var mixName = Path.GetFileNameWithoutExtension(mixFilePath);
				var mixOutputDir = Path.Combine(outputDir, mixName);
				Directory.CreateDirectory(mixOutputDir);

				using (var fs = File.OpenRead(mixFilePath))
				{
					var indentation = new string(' ', recursionLevel * 2);
					Console.WriteLine($"{indentation}Opening mix file: {mixFilePath}");
					var mix = new MixLoader.MixFile(fs, Path.GetFileName(mixFilePath), globalFilenames);
					Console.WriteLine($"{indentation}Found {mix.Contents.Count()} files in the mix");

					var extractedCount = 0;
					var nestedMixFiles = new List<string>();

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

						Console.WriteLine($"{indentation}Extracted: {filename}");
						extractedCount++;

						if (filename.EndsWith(".mix", StringComparison.OrdinalIgnoreCase))
						{
							nestedMixFiles.Add(outPath);
						}
					}

					Console.WriteLine($"{indentation}Extraction complete: {extractedCount} files extracted to {mixOutputDir}");

					if (nestedMixFiles.Count > 0)
					{
						Console.WriteLine($"{indentation}Found {nestedMixFiles.Count} nested .mix files. Processing...");
						foreach (var nestedMixFile in nestedMixFiles)
						{
							ExtractMixRecursively(nestedMixFile, outputDir, globalDbPath, recursionLevel + 1, globalFilenames);
						}
					}
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error extracting mix file: {ex.Message}");
				Console.WriteLine(ex.StackTrace);
			}
		}

		static string[] LoadGlobalFilenames(string globalDbPath, int recursionLevel)
		{
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
			else if (recursionLevel == 0)
			{
				Console.WriteLine("Warning: No global mix database specified. File identification may be limited.");
			}

			return globalFilenames;
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
				ExtractMixRecursively(mixFile, outputDir, globalDbPath);
			}

			Console.WriteLine($"\nAll mix files processed. Extracted files can be found in {outputDir}");
			Console.WriteLine($"Total unique mix files processed: {ProcessedMixFiles.Count}");
		}

		static void Main(string[] args)
		{
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
				ExtractMixRecursively(args[0], args[1], globalDbPath);
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
