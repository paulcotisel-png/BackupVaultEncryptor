using System;
using System.Security.Cryptography;
using BackupVaultEncryptor.App.Core.Models;
using BackupVaultEncryptor.App.Data;
using BackupVaultEncryptor.App.Logging;
using BackupVaultEncryptor.App.Security;

namespace BackupVaultEncryptor.App.Services
{
    /// <summary>
    /// Manages RootKeys for backup roots using the VaultMasterKey from a UserSession.
    /// </summary>
    public class VaultService
    {
        private readonly VaultKeyRepository _vaultKeyRepository;
        private readonly KeyWrapService _keyWrapService;
        private readonly AppLogger _logger;

        public VaultService(VaultKeyRepository vaultKeyRepository, KeyWrapService keyWrapService, AppLogger logger)
        {
            _vaultKeyRepository = vaultKeyRepository;
            _keyWrapService = keyWrapService;
            _logger = logger;
        }

        public RootKeyRecord GetOrCreateRootKey(UserSession session, string rootPath)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (string.IsNullOrWhiteSpace(rootPath)) throw new ArgumentException("Root path is required.", nameof(rootPath));

            // For now we just use the provided string as-is.
            var existing = _vaultKeyRepository.GetRootKey(session.UserId, rootPath);
            if (existing != null)
            {
                return existing;
            }

            var rootKey = RandomNumberGenerator.GetBytes(32);
            var wrapped = _keyWrapService.WrapWithKey(rootKey, session.VaultMasterKey);

            var record = new RootKeyRecord
            {
                UserId = session.UserId,
                RootPath = rootPath,
                RootKeyEncryptedCiphertext = wrapped.Ciphertext,
                RootKeyEncryptedNonce = wrapped.Nonce,
                RootKeyEncryptedTag = wrapped.Tag,
                CreatedUtc = DateTimeOffset.UtcNow
            };

            _vaultKeyRepository.AddRootKey(record);

            CryptographicOperations.ZeroMemory(rootKey);

            _logger.Log($"Created new RootKey for user {session.Username} and root '{rootPath}'.");

            return record;
        }

        /// <summary>
        /// Unwraps and returns the raw RootKey bytes for a given record
        /// using the VaultMasterKey from the current user session.
        /// </summary>
        public byte[] UnwrapRootKey(RootKeyRecord record, UserSession session)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (session == null) throw new ArgumentNullException(nameof(session));

            var wrapped = new WrappedKeyData(
                record.RootKeyEncryptedCiphertext,
                record.RootKeyEncryptedNonce,
                record.RootKeyEncryptedTag);

            return _keyWrapService.UnwrapWithKey(wrapped, session.VaultMasterKey);
        }
    }
}
