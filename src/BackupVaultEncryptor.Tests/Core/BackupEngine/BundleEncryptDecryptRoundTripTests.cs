using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using BackupVaultEncryptor.App.Core.BackupEngine;
using BackupVaultEncryptor.App.Infrastructure;
using BackupVaultEncryptor.App.Logging;
using BackupVaultEncryptor.App.Security;
using BackupVaultEncryptor.App.Services;
using BackupVaultEncryptor.App.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BackupVaultEncryptor.Tests.Core.BackupEngine
{
    [TestClass]
    public class BundleEncryptDecryptRoundTripTests
    {
        [TestMethod]
        public async Task EncryptThenDecrypt_RestoresFilesAndStructure()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "BVE_RoundTrip", Path.GetRandomFileName());
            var sourceRoot = Path.Combine(tempRoot, "source");
            var destRoot = Path.Combine(tempRoot, "dest");
            var jobOutputRoot = Path.Combine(tempRoot, "job");

            Directory.CreateDirectory(sourceRoot);
            Directory.CreateDirectory(destRoot);
            Directory.CreateDirectory(jobOutputRoot);

            try
            {
                var subDir = Path.Combine(sourceRoot, "FolderA");
                Directory.CreateDirectory(subDir);

                var file1 = Path.Combine(sourceRoot, "root.txt");
                var file2 = Path.Combine(subDir, "nested.txt");

                File.WriteAllText(file1, "Root file content");
                File.WriteAllText(file2, "Nested file content");

                // Set up local AppConfig and database using the same bootstrap
                // logic as the main application so VaultKeyRepository works.
                var config = new AppConfig { AppDataDirectory = tempRoot };
                var bootstrap = new SqliteBootstrap(config);
                bootstrap.InitializeDatabase();
                var logger = new AppLogger(config);
                var keyWrap = new KeyWrapService();
                var vaultRepo = new VaultKeyRepository(config);
                var vaultService = new VaultService(vaultRepo, keyWrap, logger);

                var vaultMasterKey = RandomNumberGenerator.GetBytes(32);
                var session = new UserSession(1, "testuser", vaultMasterKey);

                var scanner = new FileScanner();
                var planner = new BundlePlanner();
                var manifestStorage = new ManifestStorage();

                var encryptor = new BundleEncryptor(scanner, planner, manifestStorage, vaultService, logger);
                var decryptor = new BundleDecryptor(manifestStorage, vaultService, logger);

                var jobId = Guid.NewGuid().ToString();

                var encryptRequest = new BackupJobEncryptRequest
                {
                    JobId = jobId,
                    SourceRoot = sourceRoot,
                    DestinationRoot = jobOutputRoot,
                    TargetBundleSizeBytes = 1024 * 1024,
                    ChunkSizeBytes = 1024,
                    UserSession = session
                };

                var manifest = await encryptor.EncryptAsync(encryptRequest);

                Assert.AreEqual("Completed", manifest.Status);
                Assert.IsTrue(manifest.Bundles.Count > 0);

                var manifestPath = Path.Combine(jobOutputRoot, $"{jobId}_manifest.json");

                var decryptRequest = new BackupJobDecryptRequest
                {
                    ManifestPath = manifestPath,
                    DestinationRoot = destRoot,
                    UserSession = session
                };

                await decryptor.DecryptAsync(decryptRequest);

                var restoredFile1 = Path.Combine(destRoot, "root.txt");
                var restoredFile2 = Path.Combine(destRoot, "FolderA", "nested.txt");

                Assert.IsTrue(File.Exists(restoredFile1));
                Assert.IsTrue(File.Exists(restoredFile2));

                Assert.AreEqual("Root file content", File.ReadAllText(restoredFile1));
                Assert.AreEqual("Nested file content", File.ReadAllText(restoredFile2));
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
        }
    }
}
