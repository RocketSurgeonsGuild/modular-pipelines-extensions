using ModularPipelines.FileSystem;
using File = ModularPipelines.FileSystem.File;

namespace Rocket.Surgery.ModularPipelines.Extensions;

public static class FolderExtensions
{
    extension(Folder folder)
    {
        public static Folder operator /(Folder left, string right) => left.GetFolder(right);
        public static File operator +(Folder left, string right) => left.GetFile(right);
    }

    public static Folder EnsureExists(this Folder folder)
    {
        if (!folder.Exists)
        {
            folder.Create();
        }

        return folder;
    }
}
