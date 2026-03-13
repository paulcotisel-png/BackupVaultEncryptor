using System.Collections.Generic;

namespace BackupVaultEncryptor.App.Core.BackupEngine
{
    /// <summary>
    /// Groups files into bundles based on a target total plaintext size.
    /// Also assigns plaintext offsets for each file within its bundle.
    /// </summary>
    public class BundlePlanner
    {
        public IEnumerable<PlannedBundle> PlanBundles(IEnumerable<FileEntry> files, long targetBundleSizeBytes)
        {
            var currentBundle = new PlannedBundle(0);
            long currentSize = 0;

            foreach (var file in files)
            {
                // If adding this file would exceed the target and the current bundle
                // already has content, start a new bundle.
                if (currentSize > 0 && currentSize + file.Length > targetBundleSizeBytes)
                {
                    currentBundle.TotalPlaintextBytes = currentSize;
                    yield return currentBundle;

                    currentBundle = new PlannedBundle(currentBundle.BundleIndex + 1);
                    currentSize = 0;
                }

                var entry = new PlannedBundleEntry(file, currentSize, file.Length);
                currentBundle.Entries.Add(entry);
                currentSize += file.Length;
            }

            if (currentBundle.Entries.Count > 0)
            {
                currentBundle.TotalPlaintextBytes = currentSize;
                yield return currentBundle;
            }
        }
    }

    public class PlannedBundle
    {
        public PlannedBundle(int bundleIndex)
        {
            BundleIndex = bundleIndex;
            Entries = new List<PlannedBundleEntry>();
        }

        public int BundleIndex { get; }
        public List<PlannedBundleEntry> Entries { get; }
        public long TotalPlaintextBytes { get; set; }
    }

    public class PlannedBundleEntry
    {
        public PlannedBundleEntry(FileEntry file, long plaintextOffset, long plaintextLength)
        {
            File = file;
            PlaintextOffset = plaintextOffset;
            PlaintextLength = plaintextLength;
        }

        public FileEntry File { get; }
        public long PlaintextOffset { get; }
        public long PlaintextLength { get; }
    }
}
