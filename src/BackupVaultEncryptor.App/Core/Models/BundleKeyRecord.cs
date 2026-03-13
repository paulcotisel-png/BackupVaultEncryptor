using System;

namespace BackupVaultEncryptor.App.Core.Models
{
    /// <summary>
    /// Placeholder for future bundle key support.
    /// In this task there is no database table, repository, or crypto logic.
    /// </summary>
    public class BundleKeyRecord
    {
        public int Id { get; set; }
        public int RootKeyId { get; set; }
        public string BundleIdentifier { get; set; } = string.Empty;
        public byte[] BundleKeyEncrypted { get; set; } = Array.Empty<byte>();
    }
}
