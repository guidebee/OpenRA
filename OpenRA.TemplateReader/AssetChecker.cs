using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenRA.TemplateReader
{
    /// <summary>
    /// Utility class to check for missing original game assets
    /// </summary>
    public class AssetChecker
    {
        private readonly MixLoader mixLoader;
        private readonly ModDataLoader modDataLoader;

        /// <summary>
        /// Essential river template files that should be present in the MIX archives
        /// </summary>
        private static readonly string[] EssentialRiverTemplates = new[]
        {
            "rv01.tem", "rv02.tem", "rv03.tem", "rv04.tem", "rv05.tem", 
            "rv06.tem", "rv07.tem", "rv08.tem", "rv09.tem", "rv10.tem",
            "rv11.tem", "rv12.tem", "rv13.tem", "rv14.tem", "rv15.tem",
            "rv16.tem", "rv17.tem", "rv18.tem", "rv19.tem", "rv20.tem"
        };

        /// <summary>
        /// Essential terrain template files that should be present
        /// </summary>
        private static readonly string[] EssentialTerrainTemplates = new[]
        {
            // Just a few examples - this list should be expanded with more critical files
            "clear1.tem", "d01.tem", "d02.tem", "d03.tem", "d04.tem", "d05.tem",
            "s01.tem", "s02.tem", "s03.tem", "s04.tem", "s05.tem",
            "sh01.tem", "sh02.tem", "sh03.tem", "sh04.tem", "sh05.tem"
        };

        /// <summary>
        /// Creates a new asset checker
        /// </summary>
        /// <param name="mixLoader">The MIX loader to use for checking</param>
        /// <param name="modDataLoader">The mod data loader</param>
        public AssetChecker(MixLoader mixLoader, ModDataLoader modDataLoader)
        {
            this.mixLoader = mixLoader;
            this.modDataLoader = modDataLoader;
        }

        /// <summary>
        /// Checks if essential original game assets are available
        /// </summary>
        /// <returns>True if all essential assets are available, false otherwise</returns>
        public bool CheckEssentialAssetsAvailable()
        {
            bool allAssetsAvailable = true;
            Console.WriteLine("Checking for essential original game assets...");
            
            // Check river templates
            Console.WriteLine("Checking river templates:");
            foreach (var riverTemplate in EssentialRiverTemplates)
            {
                bool found = IsTemplateFileAvailable(riverTemplate, new[] { "temperat", "snow", "interior", "desert" });
                allAssetsAvailable &= found;
                Console.WriteLine($"  {riverTemplate}: {(found ? "Found" : "Missing")}");
            }
            
            // Check terrain templates
            Console.WriteLine("Checking terrain templates:");
            foreach (var terrainTemplate in EssentialTerrainTemplates)
            {
                bool found = IsTemplateFileAvailable(terrainTemplate, new[] { "temperat", "snow", "interior", "desert" });
                allAssetsAvailable &= found;
                Console.WriteLine($"  {terrainTemplate}: {(found ? "Found" : "Missing")}");
            }

            // Print installation instructions if assets are missing
            if (!allAssetsAvailable)
            {
                Console.WriteLine("\nSome original game assets are missing.");
                Console.WriteLine("To use OpenRA fully, you need to install the original game content.");
                Console.WriteLine("You can:");
                Console.WriteLine("1. Install the original Command & Conquer game and copy its content files to the appropriate directory.");
                Console.WriteLine("2. Use the in-game content installer to download and install a minimal asset pack.");
                Console.WriteLine("3. Download the game assets from an official digital distribution platform and install them.");
                Console.WriteLine("\nFor more information, please visit: https://github.com/OpenRA/OpenRA/wiki/Game-Content");
            }
            
            return allAssetsAvailable;
        }

        /// <summary>
        /// Gets the file extension for a tileset
        /// </summary>
        /// <param name="tileset">The tileset name</param>
        /// <returns>The file extension for the tileset</returns>
        private string GetTilesetExtension(string tileset)
        {
            if (string.IsNullOrEmpty(tileset))
            {
                return ".tem"; // Default to temperate if no tileset specified
            }

            // Check for short names first, then full names
            var lowerTileset = tileset.ToLowerInvariant();

            // Common abbreviated formats
            if (lowerTileset.StartsWith("tem")) return ".tem";
            if (lowerTileset.StartsWith("sno")) return ".sno";
            if (lowerTileset.StartsWith("des")) return ".des";
            if (lowerTileset.StartsWith("int")) return ".int";
            if (lowerTileset.StartsWith("jun")) return ".jun";

            // Full names
            return tileset.ToUpperInvariant() switch
            {
                "TEMPERAT" => ".tem",
                "SNOW" => ".sno",
                "DESERT" => ".des",
                "INTERIOR" => ".int",
                "JUNGLE" => ".jun",
                _ => ".tem" // Default to temperate
            };
        }

        /// <summary>
        /// Checks if a specific template file is available in any of the specified tilesets
        /// </summary>
        /// <param name="templateName">The template file name to check</param>
        /// <param name="tilesets">The tilesets to check</param>
        /// <returns>True if the template is found in any tileset, false otherwise</returns>
        private bool IsTemplateFileAvailable(string templateName, string[] tilesets)
        {
            foreach (var tileset in tilesets)
            {
                try
                {
                    // Try to find the template in this tileset
                    var tilesetExt = GetTilesetExtension(tileset);
                    var searchPatterns = new[]
                    {
                        templateName,
                        $"{templateName}.{tilesetExt}",
                        $"{tileset}.{templateName}",
                        $"{tileset}.{templateName}.{tilesetExt}",
                        $"{templateName}.{tilesetExt}",
                        $"{templateName.Replace(".tem", "")}.{tilesetExt}",
                        $"{templateName.Replace(".tem", "")}"
                    };

                    foreach (var pattern in searchPatterns)
                    {
                        // Check if the file exists in MIX files
                        try
                        {
                            var templateBytes = mixLoader.GetTemplateFromMix(tileset, pattern);
                            if (templateBytes != null && templateBytes.Length > 0)
                            {
                                return true;
                            }
                        }
                        catch
                        {
                            // Continue checking other patterns
                        }
                    }
                }
                catch (Exception)
                {
                    // Ignore errors and continue checking
                }
            }

            // Check general MIX files (local and conquer)
            var generalTilesets = new[] { "local", "conquer" };
            foreach (var tileset in generalTilesets)
            {
                try
                {
                    // Try to find in general MIX files
                    var templateBytes = mixLoader.GetTemplateFromMix(tileset, templateName);
                    if (templateBytes != null && templateBytes.Length > 0)
                    {
                        return true;
                    }
                }
                catch (Exception)
                {
                    // Ignore errors and continue checking
                }
            }

            return false;
        }
    }
}
