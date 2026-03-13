using System;

namespace BackupVaultEncryptor.App.Core.Models
{
    /// <summary>
    /// Represents a local user account for the application, including
    /// authentication data and wrapped vault master key fields.
    /// </summary>
    public class UserAccount
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string? RecoveryEmail { get; set; }

        /// <summary>
        /// Argon2id password hash in encoded text form.
        /// </summary>
        public string PasswordHash { get; set; } = string.Empty;

        // Vault master key wrapped with a key derived from the user's password (AES-GCM parts).
        public byte[] VaultMasterKeyWrappedCiphertext { get; set; } = Array.Empty<byte>();
        public byte[] VaultMasterKeyWrappedNonce { get; set; } = Array.Empty<byte>();
        public byte[] VaultMasterKeyWrappedTag { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// Vault master key protected with DPAPI (CurrentUser scope).
        /// </summary>
        public byte[] VaultMasterKeyProtectedByDpapi { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// Salt used when deriving the password-based wrapping key.
        /// </summary>
        public byte[] KeyWrapSalt { get; set; } = Array.Empty<byte>();

        // Password reset token (hash only) and expiry.
        public string? PasswordResetTokenHash { get; set; }
        public DateTimeOffset? PasswordResetTokenExpiresUtc { get; set; }

        public DateTimeOffset CreatedUtc { get; set; }
        public DateTimeOffset UpdatedUtc { get; set; }
    }
}
