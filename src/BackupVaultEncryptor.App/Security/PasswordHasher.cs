using System;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace BackupVaultEncryptor.App.Security
{
    /// <summary>
    /// Provides Argon2id password hashing and verification.
    /// Stores all parameters in a single beginner-readable text field.
    /// </summary>
    public class PasswordHasher
    {
        private readonly PasswordHashOptions _options;

        public PasswordHasher(PasswordHashOptions? options = null)
        {
            _options = options ?? PasswordHashOptions.Default;
        }

        public string HashPassword(string password)
        {
            if (password is null) throw new ArgumentNullException(nameof(password));

            var salt = RandomNumberGenerator.GetBytes(_options.SaltSizeBytes);
            var hashBytes = HashInternal(password, salt, _options.MemorySizeKb, _options.Iterations, _options.DegreeOfParallelism, _options.HashSizeBytes);

            var saltBase64 = Convert.ToBase64String(salt);
            var hashBase64 = Convert.ToBase64String(hashBytes);

            // Simple "key=value" style encoding for readability.
            return $"argon2id;m={_options.MemorySizeKb};t={_options.Iterations};p={_options.DegreeOfParallelism};s={saltBase64};h={hashBase64}";
        }

        public bool VerifyPassword(string password, string encodedHash)
        {
            if (password is null) throw new ArgumentNullException(nameof(password));
            if (string.IsNullOrWhiteSpace(encodedHash)) return false;

            try
            {
                // Expected format: argon2id;m=...;t=...;p=...;s=...;h=...
                var parts = encodedHash.Split(';');
                if (parts.Length != 6) return false;

                if (!parts[0].Equals("argon2id", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                int memoryKb = ParseIntPart(parts[1], 'm');
                int iterations = ParseIntPart(parts[2], 't');
                int parallelism = ParseIntPart(parts[3], 'p');

                var saltBase64 = ParseStringPart(parts[4], 's');
                var hashBase64 = ParseStringPart(parts[5], 'h');

                var salt = Convert.FromBase64String(saltBase64);
                var expectedHash = Convert.FromBase64String(hashBase64);

                var actualHash = HashInternal(password, salt, memoryKb, iterations, parallelism, expectedHash.Length);

                return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
            }
            catch
            {
                // For invalid formats or decode errors we consider verification failed.
                return false;
            }
        }

        private static int ParseIntPart(string part, char key)
        {
            // part example: "m=65536"
            var pieces = part.Split('=');
            if (pieces.Length != 2 || pieces[0].Length != 1 || pieces[0][0] != key)
            {
                throw new FormatException($"Invalid encoded hash segment '{part}' for '{key}'.");
            }

            return int.Parse(pieces[1]);
        }

        private static string ParseStringPart(string part, char key)
        {
            var pieces = part.Split('=');
            if (pieces.Length != 2 || pieces[0].Length != 1 || pieces[0][0] != key)
            {
                throw new FormatException($"Invalid encoded hash segment '{part}' for '{key}'.");
            }

            return pieces[1];
        }

        private static byte[] HashInternal(string password, byte[] salt, int memorySizeKb, int iterations, int degreeOfParallelism, int hashSizeBytes)
        {
            var passwordBytes = Encoding.UTF8.GetBytes(password);

            var argon2 = new Argon2id(passwordBytes)
            {
                Salt = salt,
                MemorySize = memorySizeKb,
                Iterations = iterations,
                DegreeOfParallelism = degreeOfParallelism
            };

            return argon2.GetBytes(hashSizeBytes);
        }
    }
}
