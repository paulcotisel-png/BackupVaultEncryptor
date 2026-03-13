using System;
using System.Security.Cryptography;

namespace BackupVaultEncryptor.App.Security
{
    /// <summary>
    /// Provides AES-GCM based key wrapping helpers.
    /// Used to wrap the VaultMasterKey and RootKeys.
    /// </summary>
    public class KeyWrapService
    {
        public WrappedKeyData WrapWithKey(byte[] keyToWrap, byte[] wrappingKey)
        {
            if (keyToWrap is null) throw new ArgumentNullException(nameof(keyToWrap));
            if (wrappingKey is null) throw new ArgumentNullException(nameof(wrappingKey));

            var nonce = RandomNumberGenerator.GetBytes(12); // 96-bit nonce recommended for GCM
            var ciphertext = new byte[keyToWrap.Length];
            var tag = new byte[16];

            using (var aesGcm = new AesGcm(wrappingKey, 16))
            {
                aesGcm.Encrypt(nonce, keyToWrap, ciphertext, tag);
            }

            return new WrappedKeyData(ciphertext, nonce, tag);
        }

        public byte[] UnwrapWithKey(WrappedKeyData wrapped, byte[] wrappingKey)
        {
            if (wrapped is null) throw new ArgumentNullException(nameof(wrapped));
            if (wrappingKey is null) throw new ArgumentNullException(nameof(wrappingKey));

            var plaintext = new byte[wrapped.Ciphertext.Length];

            using (var aesGcm = new AesGcm(wrappingKey, 16))
            {
                aesGcm.Decrypt(wrapped.Nonce, wrapped.Ciphertext, wrapped.Tag, plaintext);
            }

            return plaintext;
        }
    }

    /// <summary>
    /// Simple container for AES-GCM wrapped key data.
    /// The same structure is used both for VaultMasterKey and RootKeys.
    /// </summary>
    public class WrappedKeyData
    {
        public WrappedKeyData(byte[] ciphertext, byte[] nonce, byte[] tag)
        {
            Ciphertext = ciphertext ?? throw new ArgumentNullException(nameof(ciphertext));
            Nonce = nonce ?? throw new ArgumentNullException(nameof(nonce));
            Tag = tag ?? throw new ArgumentNullException(nameof(tag));
        }

        public byte[] Ciphertext { get; }
        public byte[] Nonce { get; }
        public byte[] Tag { get; }
    }
}
