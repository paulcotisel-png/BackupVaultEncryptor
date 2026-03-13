namespace BackupVaultEncryptor.App.Core.Models
{
    /// <summary>
    /// Simple user-facing settings for the backup engine.
    /// Values are stored in bytes but presented in the UI using GiB/MiB.
    /// </summary>
    public class UserSettings
    {
        public long BundleTargetSizeBytes { get; set; } = 4L * 1024 * 1024 * 1024; // default 4 GiB
        public int ChunkSizeBytes { get; set; } = 8 * 1024 * 1024; // default 8 MiB
    }
}
