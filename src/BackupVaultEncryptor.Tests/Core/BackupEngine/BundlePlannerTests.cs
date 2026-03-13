using System.Collections.Generic;
using BackupVaultEncryptor.App.Core.BackupEngine;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BackupVaultEncryptor.Tests.Core.BackupEngine
{
    [TestClass]
    public class BundlePlannerTests
    {
        [TestMethod]
        public void PlanBundles_AssignsPlaintextOffsets()
        {
            var files = new List<FileEntry>
            {
                new FileEntry("/tmp/a.dat", "a.dat", 10),
                new FileEntry("/tmp/b.dat", "b.dat", 20),
                new FileEntry("/tmp/c.dat", "c.dat", 30)
            };

            var planner = new BundlePlanner();
            var bundles = planner.PlanBundles(files, 25);

            var list = new List<PlannedBundle>(bundles);

            Assert.AreEqual(2, list.Count); // first bundle: a(10)+b(20)>25 so a alone; second: b+c

            var first = list[0];
            Assert.AreEqual(0, first.BundleIndex);
            Assert.AreEqual(1, first.Entries.Count);
            Assert.AreEqual(0, first.Entries[0].PlaintextOffset);
            Assert.AreEqual(10, first.Entries[0].PlaintextLength);

            var second = list[1];
            Assert.AreEqual(1, second.BundleIndex);
            Assert.AreEqual(2, second.Entries.Count);
            Assert.AreEqual(0, second.Entries[0].PlaintextOffset);
            Assert.AreEqual(20, second.Entries[0].PlaintextLength);
            Assert.AreEqual(20, second.Entries[1].PlaintextOffset);
            Assert.AreEqual(30, second.Entries[1].PlaintextLength);
        }
    }
}
