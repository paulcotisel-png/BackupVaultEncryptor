using System;

namespace BackupVaultEncryptor.App.Security
{
    /// <summary>
    /// Simple abstraction for protecting secrets on the local machine.
    /// Production uses a DPAPI-based implementation; tests can plug in a fake implementation.
    /// </summary>
    public interface ILocalSecretProtector
    {
        byte[] Protect(byte[] data);

        byte[] Unprotect(byte[] protectedData);
    }
}
