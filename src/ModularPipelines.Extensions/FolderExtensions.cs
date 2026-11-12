using ModularPipelines.FileSystem;

namespace Rocket.Surgery.ModularPipelines.Extensions;

public static class FolderExtensions
{
    extension(FolderPath folder)
    {
        public static FolderPath operator /(FolderPath left, string right) => left.GetFolder(right);
        public static FilePath operator +(FolderPath left, string right) => left.GetFile(right);
    }

    public static FolderPath EnsureExists(this FolderPath folder)
    {
        if (!folder.Exists)
        {
            folder.Create();
        }

        return folder;
    }
}
