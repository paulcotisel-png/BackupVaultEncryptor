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
    public class AuthRoundTripTests
    {
        private string _tempBaseDir = string.Empty;

        [TestInitialize]
        public void Setup()
        {
            _tempBaseDir = Path.Combine(Path.GetTempPath(), "BackupVaultEncryptorAuthRoundTripTests", Guid.NewGuid().ToString("N"));
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
        public void UserRepository_PasswordHash_RoundTripsThroughSqlite()
        {
            var appConfig = new AppConfig
            {
                AppDataDirectory = _tempBaseDir,
                DatabaseFileName = "repo-roundtrip.db",
                LogFileName = "repo-roundtrip.log"
            };

            var sqliteBootstrap = new SqliteBootstrap(appConfig);
            sqliteBootstrap.InitializeDatabase();

            var passwordHasher = new PasswordHasher();
            var userRepository = new UserRepository(appConfig);

            var username = "roundtrip-user";
            var password = "RoundTripPassword!42";

            var hashBefore = passwordHasher.HashPassword(password);

            var now = DateTimeOffset.UtcNow;
            var user = new UserAccount
            {
                Username = username,
                PasswordHash = hashBefore,
                RecoveryEmail = null,
                VaultMasterKeyWrappedCiphertext = new byte[] { 1 },
                VaultMasterKeyWrappedNonce = new byte[] { 2 },
                VaultMasterKeyWrappedTag = new byte[] { 3 },
                VaultMasterKeyProtectedByDpapi = new byte[] { 4 },
                KeyWrapSalt = new byte[] { 5 },
                CreatedUtc = now,
                UpdatedUtc = now
            };

            userRepository.CreateUser(user);

            var stored = userRepository.GetByUsername(username);
            Assert.IsNotNull(stored, "Stored user should not be null.");

            var hashAfter = stored!.PasswordHash;

            Assert.AreEqual(hashBefore.Length, hashAfter.Length, "Password hash length should round-trip unchanged.");
            Assert.AreEqual(hashBefore, hashAfter, "Password hash string should round-trip unchanged.");
            Assert.IsTrue(passwordHasher.VerifyPassword(password, hashAfter), "Password should verify after SQLite round-trip.");
        }

        [TestMethod]
        public void AuthService_RegisterAndLogin_RoundTripSucceeds()
        {
            var appConfig = new AppConfig
            {
                AppDataDirectory = _tempBaseDir,
                DatabaseFileName = "auth-roundtrip.db",
                LogFileName = "auth-roundtrip.log"
            };

            var sqliteBootstrap = new SqliteBootstrap(appConfig);
            sqliteBootstrap.InitializeDatabase();

            var logger = new AppLogger(appConfig);
            var userRepository = new UserRepository(appConfig);
            var passwordHasher = new PasswordHasher();
            var passwordKeyDeriver = new PasswordKeyDeriver();
            var keyWrapService = new KeyWrapService();
            var localSecretProtector = new FakeLocalSecretProtector();

            var authService = new AuthService(userRepository, passwordHasher, passwordKeyDeriver, keyWrapService, localSecretProtector, logger);

            var username = "roundtrip-auth-user";
            var password = "AuthRoundTripPassword!123";

            var registeredUser = authService.RegisterUser(username, password);

            var storedUser = userRepository.GetByUsername(username);
            Assert.IsNotNull(storedUser, "Stored user should not be null after registration.");

            Assert.AreEqual(registeredUser.PasswordHash.Length, storedUser!.PasswordHash.Length, "Hash length should be unchanged after registration.");
            Assert.AreEqual(registeredUser.PasswordHash, storedUser.PasswordHash, "Hash string should be the same before and after SQLite.");
            Assert.IsTrue(passwordHasher.VerifyPassword(password, storedUser.PasswordHash), "Password should verify against stored hash.");

            var session = authService.Login(username, password);
            Assert.IsNotNull(session, "Login should succeed immediately after registration.");
            Assert.AreEqual(username, session!.Username, "Session should contain the correct username.");
        }
    }
}
