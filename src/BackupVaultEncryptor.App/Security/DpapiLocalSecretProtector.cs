using System;
using System.Security.Cryptography;

namespace BackupVaultEncryptor.App.Security
{
    /// <summary>
    /// Uses Windows DPAPI (CurrentUser scope) to protect secrets on the local machine.
    /// </summary>
    public class DpapiLocalSecretProtector : ILocalSecretProtector
    {
        public byte[] Protect(byte[] data)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            return ProtectedData.Protect(data, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
        }

        public byte[] Unprotect(byte[] protectedData)
        {
            if (protectedData is null) throw new ArgumentNullException(nameof(protectedData));
            return ProtectedData.Unprotect(protectedData, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
        }
    }
}
