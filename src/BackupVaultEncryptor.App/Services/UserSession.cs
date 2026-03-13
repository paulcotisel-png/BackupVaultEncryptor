using System;

namespace BackupVaultEncryptor.App.Services
{
    /// <summary>
    /// Represents an authenticated user session, including the unwrapped VaultMasterKey
    /// for use in vault operations during the session.
    /// </summary>
    public class UserSession
    {
        public int UserId { get; }
        public string Username { get; }
        public byte[] VaultMasterKey { get; }

        public UserSession(int userId, string username, byte[] vaultMasterKey)
        {
            UserId = userId;
            Username = username ?? throw new ArgumentNullException(nameof(username));
            VaultMasterKey = vaultMasterKey ?? throw new ArgumentNullException(nameof(vaultMasterKey));
        }
    }
}
