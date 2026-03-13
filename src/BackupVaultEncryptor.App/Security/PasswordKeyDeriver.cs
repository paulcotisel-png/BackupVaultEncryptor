using System;
using System.Text;
using Konscious.Security.Cryptography;

namespace BackupVaultEncryptor.App.Security
{
    /// <summary>
    /// Derives symmetric keys from a password using Argon2id.
    /// This is separate from PasswordHasher so we can clearly separate concerns.
    /// </summary>
    public class PasswordKeyDeriver
    {
        private readonly PasswordHashOptions _options;

        public PasswordKeyDeriver(PasswordHashOptions? options = null)
        {
            _options = options ?? PasswordHashOptions.Default;
        }

        public byte[] DeriveKey(string password, byte[] salt, int keySizeBytes, string purpose)
        {
            if (password is null) throw new ArgumentNullException(nameof(password));
            if (salt is null) throw new ArgumentNullException(nameof(salt));
            if (string.IsNullOrWhiteSpace(purpose)) throw new ArgumentException("Purpose must be provided.", nameof(purpose));

            // Mix the purpose string into the salt so that keys for different purposes
            // are not interchangeable even with the same password + base salt.
            var purposeBytes = Encoding.UTF8.GetBytes(purpose);
            var combinedSalt = new byte[salt.Length + purposeBytes.Length];
            Buffer.BlockCopy(salt, 0, combinedSalt, 0, salt.Length);
            Buffer.BlockCopy(purposeBytes, 0, combinedSalt, salt.Length, purposeBytes.Length);

            var passwordBytes = Encoding.UTF8.GetBytes(password);

            var argon2 = new Argon2id(passwordBytes)
            {
                Salt = combinedSalt,
                MemorySize = _options.MemorySizeKb,
                Iterations = _options.Iterations,
                DegreeOfParallelism = _options.DegreeOfParallelism
            };

            return argon2.GetBytes(keySizeBytes);
        }
    }
}
