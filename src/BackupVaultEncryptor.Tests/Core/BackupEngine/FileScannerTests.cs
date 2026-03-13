using System.IO;
using BackupVaultEncryptor.App.Core.BackupEngine;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BackupVaultEncryptor.Tests.Core.BackupEngine
{
    [TestClass]
    public class FileScannerTests
    {
        [TestMethod]
        public void ScanFiles_ProducesRelativePaths()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "BVE_FileScannerTests", Path.GetRandomFileName());
            Directory.CreateDirectory(tempRoot);

            try
            {
                var subDir = Path.Combine(tempRoot, "SubFolder");
                Directory.CreateDirectory(subDir);

                var file1 = Path.Combine(tempRoot, "file1.txt");
                var file2 = Path.Combine(subDir, "file2.txt");

                File.WriteAllText(file1, "Test1");
                File.WriteAllText(file2, "Test2");

                var scanner = new FileScanner();
                var entries = scanner.ScanFiles(tempRoot);

                bool sawFile1 = false;
                bool sawFile2 = false;

                foreach (var e in entries)
                {
                    if (e.FullPath == file1)
                    {
                        sawFile1 = true;
                        Assert.AreEqual("file1.txt", e.RelativePath);
                    }
                    else if (e.FullPath == file2)
                    {
                        sawFile2 = true;
                        Assert.AreEqual(Path.Combine("SubFolder", "file2.txt"), e.RelativePath);
                    }
                }

                Assert.IsTrue(sawFile1, "Expected to see file1.txt in scan results.");
                Assert.IsTrue(sawFile2, "Expected to see SubFolder/file2.txt in scan results.");
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
