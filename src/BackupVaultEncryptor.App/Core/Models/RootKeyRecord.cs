using System;

namespace BackupVaultEncryptor.App.Core.Models
{
    /// <summary>
    /// Represents a RootKey for a specific backup root / PC root folder.
    /// The key itself is stored encrypted with the VaultMasterKey (AES-GCM parts).
    /// </summary>
    public class RootKeyRecord
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string RootPath { get; set; } = string.Empty;

        public byte[] RootKeyEncryptedCiphertext { get; set; } = Array.Empty<byte>();
        public byte[] RootKeyEncryptedNonce { get; set; } = Array.Empty<byte>();
        public byte[] RootKeyEncryptedTag { get; set; } = Array.Empty<byte>();

        public DateTimeOffset CreatedUtc { get; set; }
    }
}
