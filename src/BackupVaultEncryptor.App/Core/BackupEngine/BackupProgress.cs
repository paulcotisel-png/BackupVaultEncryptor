using System;

namespace BackupVaultEncryptor.App.Core.BackupEngine
{
    /// <summary>
    /// Simple progress model reported by the backup engine for encrypt/decrypt jobs.
    /// UI can consume this to show percentage and basic status text.
    /// </summary>
    public class BackupProgress
    {
        public string Phase { get; set; } = string.Empty;

        public long ProcessedBytes { get; set; }

        public long TotalBytes { get; set; }

        /// <summary>
        /// Convenience property for a 0-100 percentage based on processed and total bytes.
        /// </summary>
        public double PercentComplete
        {
            get
            {
                if (TotalBytes <= 0 || ProcessedBytes <= 0)
                {
                    return 0;
                }

                // Use double for a smooth value; UI can clamp to [0,100].
                return (ProcessedBytes * 100.0) / TotalBytes;
            }
        }

        public int CurrentBundleIndex { get; set; }

        public int TotalBundles { get; set; }

        /// <summary>
        /// Optional friendly name for the current item (file or bundle).
        /// </summary>
        public string? CurrentItemName { get; set; }
    }
}
