using System;
using System.Collections.Generic;

namespace BackupVaultEncryptor.App.Core.Models
{
    /// <summary>
    /// Plaintext manifest describing a backup job and its bundles.
    /// NOTE: In v1 this file is NOT encrypted and therefore leaks metadata
    /// such as the source root, relative file paths, and file sizes.
    /// Keep the contents minimal and avoid storing unnecessary extra data.
    /// </summary>
    public class BackupJobManifest
    {
        public int Version { get; set; } = 1;
        public string JobId { get; set; } = string.Empty;
        public string SourceRoot { get; set; } = string.Empty;
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public long BundleTargetSizeBytes { get; set; }
        public int ChunkSizeBytes { get; set; }
        public string Status { get; set; } = "InProgress";
        public List<BackupBundleInfo> Bundles { get; set; } = new List<BackupBundleInfo>();
    }

    public class BackupBundleInfo
    {
        public int BundleIndex { get; set; }
        public string BundleFileName { get; set; } = string.Empty;
        public long TotalPlaintextBytes { get; set; }
        public long BundleFileSizeBytes { get; set; }
        public bool IsPublished { get; set; }
        public List<BackupBundleEntry> Entries { get; set; } = new List<BackupBundleEntry>();
    }

    public class BackupBundleEntry
    {
        public string RelativePath { get; set; } = string.Empty;
        public long OriginalSizeBytes { get; set; }
        public int BundleIndex { get; set; }
        public long PlaintextOffset { get; set; }
        public long PlaintextLength { get; set; }
    }
}
