using System;
using System.IO;

namespace OpenRA.TemplateReader
{
	public class ModLoader
	{
		public ModData LoadModData(string modId, string gamePath)
		{
			Game.InitializeSettings(Arguments.Empty);
			var modSearchPaths = new[] { Path.Combine(gamePath, "mods") };
			var mods = new InstalledMods(modSearchPaths, new[] { modId });
			if (!mods.ContainsKey(modId))
				throw new Exception($"Mod '{modId}' not found in path '{modSearchPaths[0]}'");

			var manifest = mods[modId];
			var modData = new ModData(manifest, mods, true);
			return modData;
		}
	}
}
