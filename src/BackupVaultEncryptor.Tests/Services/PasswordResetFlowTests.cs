using System;
using System.IO;
using BackupVaultEncryptor.App.Core.Models;
using BackupVaultEncryptor.App.Data;
using BackupVaultEncryptor.App.Infrastructure;
using BackupVaultEncryptor.App.Logging;
using BackupVaultEncryptor.App.Security;
using BackupVaultEncryptor.App.Services;
using BackupVaultEncryptor.Tests.Security;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BackupVaultEncryptor.Tests.Services
{
    [TestClass]
    public class PasswordResetFlowTests
    {
        private string _tempBaseDir = string.Empty;

        [TestInitialize]
        public void Setup()
        {
            _tempBaseDir = Path.Combine(Path.GetTempPath(), "BackupVaultEncryptorTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempBaseDir);
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_tempBaseDir))
            {
                try
                {
                    Directory.Delete(_tempBaseDir, recursive: true);
                }
                catch
                {
                    // Ignore cleanup errors in tests.
                }
            }
        }

        [TestMethod]
        public void ChangePassword_RewrapsVaultMasterKey_ButKeepsPlaintextSame()
        {
            var services = CreateAuthService();

            var user = services.Auth.RegisterUser("alice", "OldPassword!123");

            // Capture the plaintext VMK via a login.
            var session1 = services.Auth.Login("alice", "OldPassword!123");
            Assert.IsNotNull(session1);
            var vmkBefore = (byte[])session1!.VaultMasterKey.Clone();

            services.Auth.ChangePassword(user.Id, "OldPassword!123", "NewPassword!456");

            // Log in with new password and compare VMK.
            var session2 = services.Auth.Login("alice", "NewPassword!456");
            Assert.IsNotNull(session2);
            var vmkAfter = session2!.VaultMasterKey;

            CollectionAssert.AreEqual(vmkBefore, vmkAfter);
        }

        private (AuthService Auth, VaultService Vault) CreateAuthService()
        {
            var appConfig = new AppConfig
            {
                AppDataDirectory = _tempBaseDir,
                DatabaseFileName = "test.db",
                LogFileName = "test.log"
            };

            var sqliteBootstrap = new SqliteBootstrap(appConfig);
            sqliteBootstrap.InitializeDatabase();

            var logger = new AppLogger(appConfig);
            var userRepository = new UserRepository(appConfig);
            var vaultKeyRepository = new VaultKeyRepository(appConfig);

            var passwordHasher = new PasswordHasher();
            var passwordKeyDeriver = new PasswordKeyDeriver();
            var keyWrapService = new KeyWrapService();

            var localSecretProtector = new FakeLocalSecretProtector();

            var authService = new AuthService(userRepository, passwordHasher, passwordKeyDeriver, keyWrapService, localSecretProtector, logger);
            var vaultService = new VaultService(vaultKeyRepository, keyWrapService, logger);

            return (authService, vaultService);
        }
    }
}
