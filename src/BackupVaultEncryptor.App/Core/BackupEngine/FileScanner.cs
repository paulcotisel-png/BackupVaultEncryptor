using System.Collections.Generic;
using System.IO;

namespace BackupVaultEncryptor.App.Core.BackupEngine
{
    /// <summary>
    /// Simple helper that scans a source root for files and produces
    /// entries with full and relative paths. Works for local and SMB paths
    /// using normal filesystem access.
    /// </summary>
    public class FileScanner
    {
        public IEnumerable<FileEntry> ScanFiles(string sourceRoot)
        {
            // Normal Directory APIs work for both local paths and SMB UNC paths
            // as long as the current Windows user has access.
            foreach (var fullPath in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
            {
                var info = new FileInfo(fullPath);
                if (!info.Exists)
                {
                    continue;
                }

                var relative = Path.GetRelativePath(sourceRoot, fullPath);

                yield return new FileEntry(fullPath, relative, info.Length);
            }
        }
    }

    public class FileEntry
    {
        public FileEntry(string fullPath, string relativePath, long length)
        {
            FullPath = fullPath;
            RelativePath = relativePath;
            Length = length;
        }

        public string FullPath { get; }
        public string RelativePath { get; }
        public long Length { get; }
    }
}
