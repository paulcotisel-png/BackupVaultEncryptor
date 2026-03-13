using System;

namespace BackupVaultEncryptor.App.Security
{
    /// <summary>
    /// Options used for Argon2id password hashing and key derivation.
    /// Kept simple and explicit so it's easy to tweak for different machines.
    /// </summary>
    public class PasswordHashOptions
    {
        public int MemorySizeKb { get; set; } = 64 * 1024; // 64 MB
        public int Iterations { get; set; } = 3;
        public int DegreeOfParallelism { get; set; } = 2;
        public int SaltSizeBytes { get; set; } = 16;
        public int HashSizeBytes { get; set; } = 32;

        public static PasswordHashOptions Default => new PasswordHashOptions();
    }
}
