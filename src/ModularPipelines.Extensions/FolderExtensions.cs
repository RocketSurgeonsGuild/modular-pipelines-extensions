using ModularPipelines.FileSystem;
using File = ModularPipelines.FileSystem.File;

namespace build.library;

public static class FolderExtensions
{
    extension(Folder folder)
    {
        public static Folder operator /(Folder left, string right) => left.GetFolder(right);
        public static File operator +(Folder left, string right) => left.GetFile(right);
    }
}
