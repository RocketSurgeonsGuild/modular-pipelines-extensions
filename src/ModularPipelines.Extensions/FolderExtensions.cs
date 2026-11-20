namespace Rocket.Surgery.ModularPipelines.Extensions;

/// <summary>Path helpers for <see cref="FolderPath" />.</summary>
public static class FolderExtensions
{
    extension(FolderPath folder)
    {
        /// <summary>The sub folder <paramref name="right" /> of <paramref name="left" />.</summary>
        /// <param name="left">The parent folder.</param>
        /// <param name="right">The sub folder's relative path.</param>
        public static FolderPath operator /(FolderPath left, string right) => left.GetFolder(right);
        /// <summary>The file <paramref name="right" /> in <paramref name="left" />.</summary>
        /// <param name="left">The folder.</param>
        /// <param name="right">The file's relative path.</param>
        public static FilePath operator +(FolderPath left, string right) => left.GetFile(right);
    }

    /// <summary>Creates the folder if it does not exist.</summary>
    /// <param name="folder">The folder.</param>
    /// <returns>The same folder, for chaining.</returns>
    public static FolderPath EnsureExists(this FolderPath folder)
    {
        if (!folder.Exists)
        {
            folder.Create();
        }

        return folder;
    }
}
