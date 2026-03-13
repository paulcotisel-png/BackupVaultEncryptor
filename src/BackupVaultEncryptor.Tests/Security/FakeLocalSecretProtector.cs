using System;
using BackupVaultEncryptor.App.Security;

namespace BackupVaultEncryptor.Tests.Security
{
    /// <summary>
    /// Simple in-memory implementation of ILocalSecretProtector for tests.
    /// It does not provide real security but keeps tests deterministic.
    /// </summary>
    public class FakeLocalSecretProtector : ILocalSecretProtector
    {
        public byte[] Protect(byte[] data)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            // Just clone the array; in tests we care about round-tripping, not OS protection.
            var copy = new byte[data.Length];
            Buffer.BlockCopy(data, 0, copy, 0, data.Length);
            return copy;
        }

        public byte[] Unprotect(byte[] protectedData)
        {
            if (protectedData is null) throw new ArgumentNullException(nameof(protectedData));
            var copy = new byte[protectedData.Length];
            Buffer.BlockCopy(protectedData, 0, copy, 0, protectedData.Length);
            return copy;
        }
    }
}
