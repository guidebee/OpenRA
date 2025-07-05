using System.Collections.Generic;
using OpenRA.FileSystem;
using FS = OpenRA.FileSystem.FileSystem;

namespace OpenRA.TemplateReader
{
	public class CustomFileSystemLoader : IFileSystemLoader
	{
		public void Mount(FS fileSystem, ObjectCreator objectCreator)
		{
			// Just mount basic folders needed for template reading
			// No need to mount game-specific content that requires installation
			fileSystem.Mount(".", null);
		}
	}
}
