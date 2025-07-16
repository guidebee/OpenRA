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
using System.IO;
using System.Linq;

namespace OpenRA.Mods.Common.UtilityCommands
{
	sealed class ExtractAllFilesCommand : IUtilityCommand
	{
		string IUtilityCommand.Name => "--extract-all";

		bool IUtilityCommand.ValidateArguments(string[] args)
		{
			return args.Length >= 2;
		}

		[Desc("Extract all files from mod packages to the current directory, preserving folder structure")]
		void IUtilityCommand.Run(Utility utility, string[] args)
		{
			var fileSystem = utility.ModData.DefaultFileSystem;

			// Get all mounted packages if possible
			var fsType = fileSystem.GetType();
			var mountedPackagesProp = fsType.GetProperty("MountedPackages");
			if (mountedPackagesProp == null)
				throw new InvalidOperationException("Cannot enumerate packages in DefaultFileSystem");

			var mountedPackages = mountedPackagesProp.GetValue(fileSystem) as System.Collections.IEnumerable;
			if (mountedPackages == null)
				throw new InvalidOperationException("No packages found in DefaultFileSystem");

			foreach (var packageObj in mountedPackages)
			{
				var packageType = packageObj.GetType();
				var contentsProp = packageType.GetProperty("Contents");
				if (contentsProp == null) continue;
				var contents = contentsProp.GetValue(packageObj) as System.Collections.IEnumerable;
				if (contents == null) continue;

				foreach (var fileObj in contents)
				{
					var file = fileObj as string;
					if (string.IsNullOrEmpty(file)) continue;

					// Open file from filesystem
					try
					{
						using var src = fileSystem.Open(file);
						if (src == null) continue;
						var data = new byte[src.Length];
						src.Read(data, 0, data.Length);

						// Ensure directory exists
						var dir = Path.GetDirectoryName(file);
						if (!string.IsNullOrEmpty(dir))
							Directory.CreateDirectory(dir);

						File.WriteAllBytes($"./ra-content/{file}", data);
						Console.WriteLine(file + " saved.");
					}
					catch (Exception ex)
					{
						Console.WriteLine($"Failed to extract {file}: {ex.Message}");
					}
				}
			}
		}
	}
}
