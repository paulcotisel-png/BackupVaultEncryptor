using System;
using System.Security.Cryptography;
using BackupVaultEncryptor.App.Core.BackupEngine;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BackupVaultEncryptor.Tests.Core.BackupEngine
{
    [TestClass]
    public class BundleKeyDerivationTests
    {
        [TestMethod]
        public void DeriveBundleKey_IsDeterministic()
        {
            var rootKey = RandomNumberGenerator.GetBytes(32);
            var jobId = Guid.NewGuid().ToString();
            var index = 5;

            var k1 = BundleKeyDerivation.DeriveBundleKey(rootKey, jobId, index);
            var k2 = BundleKeyDerivation.DeriveBundleKey(rootKey, jobId, index);

            CollectionAssert.AreEqual(k1, k2);
        }

        [TestMethod]
        public void DeriveBundleKey_DiffersForDifferentIndexes()
        {
            var rootKey = RandomNumberGenerator.GetBytes(32);
            var jobId = Guid.NewGuid().ToString();

            var k1 = BundleKeyDerivation.DeriveBundleKey(rootKey, jobId, 1);
            var k2 = BundleKeyDerivation.DeriveBundleKey(rootKey, jobId, 2);

            CollectionAssert.AreNotEqual(k1, k2);
        }
    }
}
