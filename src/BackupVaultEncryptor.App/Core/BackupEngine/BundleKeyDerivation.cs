using System.Security.Cryptography;
using System.Text;

namespace BackupVaultEncryptor.App.Core.BackupEngine
{
    /// <summary>
    /// Deterministic key derivation using HMAC-SHA256.
    /// Produces a per-bundle key from the RootKey, JobId, and BundleIndex.
    /// This is intentionally simple and explicit rather than a full HKDF.
    /// </summary>
    public static class BundleKeyDerivation
    {
        public static byte[] DeriveBundleKey(byte[] rootKey, string jobId, int bundleIndex)
        {
            var input = $"BundleKey|{jobId}|{bundleIndex}";
            var inputBytes = Encoding.UTF8.GetBytes(input);

            using var hmac = new HMACSHA256(rootKey);
            var hash = hmac.ComputeHash(inputBytes);

            // Use first 32 bytes (full HMAC-SHA256 output) as the bundle key.
            var key = new byte[32];
            Buffer.BlockCopy(hash, 0, key, 0, key.Length);
            return key;
        }
    }
}
