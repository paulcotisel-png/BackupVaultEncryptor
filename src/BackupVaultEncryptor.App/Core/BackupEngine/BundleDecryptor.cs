using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using BackupVaultEncryptor.App.Core.Models;
using BackupVaultEncryptor.App.Logging;
using BackupVaultEncryptor.App.Services;

namespace BackupVaultEncryptor.App.Core.BackupEngine
{
    /// <summary>
    /// Decrypts bundle files created by BundleEncryptor and restores
    /// the original folder and file structure.
    /// </summary>
    public class BundleDecryptor
    {
        private readonly ManifestStorage _manifestStorage;
        private readonly VaultService _vaultService;
        private readonly AppLogger _logger;

        public BundleDecryptor(ManifestStorage manifestStorage, VaultService vaultService, AppLogger logger)
        {
            _manifestStorage = manifestStorage;
            _vaultService = vaultService;
            _logger = logger;
        }

        public async Task DecryptAsync(BackupJobDecryptRequest request, IProgress<BackupProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            var manifest = await _manifestStorage.LoadAsync(request.ManifestPath, cancellationToken).ConfigureAwait(false);

            var rootKeyRecord = _vaultService.GetOrCreateRootKey(request.UserSession, manifest.SourceRoot);
            var rootKey = _vaultService.UnwrapRootKey(rootKeyRecord, request.UserSession);

            try
            {
                var totalBytes = manifest.Bundles.Sum(b => b.TotalPlaintextBytes);
                var totalBundles = manifest.Bundles.Count;

                // When resuming in the future, completed bundles could be counted here.
                long processedBytes = 0;

                for (var i = 0; i < manifest.Bundles.Count; i++)
                {
                    var bundle = manifest.Bundles[i];
                    cancellationToken.ThrowIfCancellationRequested();
                    var currentBundleIndex = i + 1;

                    if (totalBytes > 0 && progress != null)
                    {
                        progress.Report(new BackupProgress
                        {
                            Phase = "Decrypting",
                            ProcessedBytes = processedBytes,
                            TotalBytes = totalBytes,
                            CurrentBundleIndex = currentBundleIndex,
                            TotalBundles = totalBundles,
                            CurrentItemName = bundle.BundleFileName
                        });
                    }

                    await DecryptBundleAsync(
                        manifest.JobId,
                        bundle,
                        rootKey,
                        Path.GetDirectoryName(request.ManifestPath) ?? string.Empty,
                        request.DestinationRoot,
                        totalBytes,
                        processedBytes,
                        currentBundleIndex,
                        totalBundles,
                        progress,
                        cancellationToken).ConfigureAwait(false);

                    processedBytes += bundle.TotalPlaintextBytes;
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(rootKey);
            }
        }

        private async Task DecryptBundleAsync(
            string jobId,
            BackupBundleInfo bundle,
            byte[] rootKey,
            string manifestDirectory,
            string destinationRoot,
            long totalBytes,
            long processedBytesBase,
            int currentBundleIndex,
            int totalBundles,
            IProgress<BackupProgress>? progress,
            CancellationToken cancellationToken)
        {
            var bundleKey = BundleKeyDerivation.DeriveBundleKey(rootKey, jobId, bundle.BundleIndex);

            try
            {
                var bundlePath = Path.Combine(manifestDirectory, bundle.BundleFileName);
                if (!File.Exists(bundlePath))
                {
                    _logger.Log($"Bundle file not found: {bundlePath}");
                    return;
                }

                await using var stream = new FileStream(bundlePath, FileMode.Open, FileAccess.Read, FileShare.Read);

                // Read and validate header
                var header = ReadHeader(stream);

                if (header.JobId != jobId || header.BundleIndex != bundle.BundleIndex)
                {
                    throw new InvalidOperationException("Bundle header does not match manifest.");
                }

                using var aesGcm = new AesGcm(bundleKey, 16);

                var chunkSizeBytes = header.ChunkSizeBytes;
                var ciphertextBuffer = new byte[chunkSizeBytes];
                var plaintextBuffer = new byte[chunkSizeBytes];
                var tagBuffer = new byte[16];
                var lengthBytes = new byte[4];
                var chunkCounter = 0;

                long bundleProcessed = 0;

                // Prepare file writers keyed by relative path
                var entryMap = new Dictionary<string, (BackupBundleEntry Entry, FileStream Stream)>();

                try
                {
                    while (true)
                    {
                        // Read PlaintextLength
                        var read = await stream.ReadAsync(lengthBytes, 0, lengthBytes.Length, cancellationToken).ConfigureAwait(false);
                        if (read == 0)
                        {
                            break; // EOF
                        }

                        if (read != lengthBytes.Length)
                        {
                            throw new InvalidDataException("Unexpected end of stream while reading chunk length.");
                        }

                        var plaintextLength = BitConverter.ToInt32(lengthBytes, 0);
                        if (plaintextLength <= 0)
                        {
                            break;
                        }

                        // Read ciphertext and tag
                        var bytesToRead = plaintextLength;
                        var offset = 0;
                        while (bytesToRead > 0)
                        {
                            var n = await stream.ReadAsync(ciphertextBuffer, offset, bytesToRead, cancellationToken).ConfigureAwait(false);
                            if (n <= 0)
                            {
                                throw new InvalidDataException("Unexpected end of stream while reading ciphertext.");
                            }
                            offset += n;
                            bytesToRead -= n;
                        }

                        var tagRead = await stream.ReadAsync(tagBuffer, 0, tagBuffer.Length, cancellationToken).ConfigureAwait(false);
                        if (tagRead != tagBuffer.Length)
                        {
                            throw new InvalidDataException("Unexpected end of stream while reading authentication tag.");
                        }

                        var nonce = BuildNonce(header.NoncePrefix, chunkCounter);
                        aesGcm.Decrypt(nonce, ciphertextBuffer.AsSpan(0, plaintextLength), tagBuffer, plaintextBuffer.AsSpan(0, plaintextLength));

                        // Determine which entries this plaintext chunk contributes to
                        WritePlaintextToFiles(bundle, destinationRoot, plaintextBuffer, plaintextLength, entryMap);

                        bundleProcessed += plaintextLength;

                        if (totalBytes > 0 && progress != null)
                        {
                            var currentProcessed = processedBytesBase + bundleProcessed;

                            progress.Report(new BackupProgress
                            {
                                Phase = "Decrypting",
                                ProcessedBytes = currentProcessed,
                                TotalBytes = totalBytes,
                                CurrentBundleIndex = currentBundleIndex,
                                TotalBundles = totalBundles,
                                CurrentItemName = bundle.BundleFileName
                            });
                        }

                        chunkCounter++;
                    }
                }
                finally
                {
                    foreach (var kvp in entryMap)
                    {
                        kvp.Value.Stream.Dispose();
                    }
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bundleKey);
            }
        }

        private static void WritePlaintextToFiles(BackupBundleInfo bundle, string destinationRoot, byte[] plaintextChunk, int plaintextLength, Dictionary<string, (BackupBundleEntry Entry, FileStream Stream)> entryMap)
        {
            // We walk entries in order of PlaintextOffset and stream bytes into the
            // appropriate file based on how much has already been written.
            // To keep the implementation simple and deterministic, we do not
            // attempt mid-bundle resume; reruns will simply overwrite files.

            var chunkPosition = 0;

            foreach (var entry in bundle.Entries)
            {
                var key = entry.RelativePath;

                if (!entryMap.TryGetValue(key, out var state))
                {
                    var fullPath = Path.Combine(destinationRoot, entry.RelativePath);
                    var directory = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    state = (entry, stream);
                    entryMap[key] = state;
                }

                var remainingForEntry = state.Entry.OriginalSizeBytes - state.Stream.Length;
                if (remainingForEntry <= 0)
                {
                    continue;
                }

                var bytesAvailableInChunk = plaintextLength - chunkPosition;
                if (bytesAvailableInChunk <= 0)
                {
                    break;
                }

                var toWrite = (int)Math.Min(remainingForEntry, bytesAvailableInChunk);
                state.Stream.Write(plaintextChunk, chunkPosition, toWrite);
                chunkPosition += toWrite;

                if (chunkPosition >= plaintextLength)
                {
                    break;
                }
            }
        }

        private static BundleHeader ReadHeader(Stream stream)
        {
            var magic = new byte[4];
            if (stream.Read(magic, 0, magic.Length) != magic.Length)
            {
                throw new InvalidDataException("Invalid bundle header (magic).");
            }

            if (magic[0] != (byte)'B' || magic[1] != (byte)'V' || magic[2] != (byte)'E' || magic[3] != (byte)'B')
            {
                throw new InvalidDataException("Unexpected bundle magic.");
            }

            var versionBytes = new byte[2];
            if (stream.Read(versionBytes, 0, 2) != 2)
            {
                throw new InvalidDataException("Invalid bundle header (version).");
            }

            var version = BitConverter.ToInt16(versionBytes, 0);
            if (version != 1)
            {
                throw new InvalidDataException($"Unsupported bundle version: {version}");
            }

            // Reserved/flags (2 bytes)
            var reserved = new byte[2];
            if (stream.Read(reserved, 0, 2) != 2)
            {
                throw new InvalidDataException("Invalid bundle header (reserved).");
            }

            var guidBytes = new byte[16];
            if (stream.Read(guidBytes, 0, guidBytes.Length) != guidBytes.Length)
            {
                throw new InvalidDataException("Invalid bundle header (JobId).");
            }

            var jobId = new Guid(guidBytes).ToString();

            var indexBytes = new byte[4];
            if (stream.Read(indexBytes, 0, 4) != 4)
            {
                throw new InvalidDataException("Invalid bundle header (BundleIndex).");
            }

            var bundleIndex = BitConverter.ToInt32(indexBytes, 0);

            var chunkSizeBytes = new byte[4];
            if (stream.Read(chunkSizeBytes, 0, 4) != 4)
            {
                throw new InvalidDataException("Invalid bundle header (ChunkSizeBytes).");
            }

            var chunkSize = BitConverter.ToInt32(chunkSizeBytes, 0);

            var noncePrefix = new byte[8];
            if (stream.Read(noncePrefix, 0, noncePrefix.Length) != noncePrefix.Length)
            {
                throw new InvalidDataException("Invalid bundle header (NoncePrefix).");
            }

            return new BundleHeader(jobId, bundleIndex, chunkSize, noncePrefix);
        }

        private static byte[] BuildNonce(byte[] prefix, int chunkCounter)
        {
            var nonce = new byte[12];
            Buffer.BlockCopy(prefix, 0, nonce, 0, prefix.Length);
            var counterBytes = BitConverter.GetBytes(chunkCounter);
            Buffer.BlockCopy(counterBytes, 0, nonce, prefix.Length, 4);
            return nonce;
        }
    }

    public class BackupJobDecryptRequest
    {
        public string ManifestPath { get; set; } = string.Empty;
        public string DestinationRoot { get; set; } = string.Empty;
        public UserSession UserSession { get; set; } = null!;
    }

    internal class BundleHeader
    {
        public BundleHeader(string jobId, int bundleIndex, int chunkSizeBytes, byte[] noncePrefix)
        {
            JobId = jobId;
            BundleIndex = bundleIndex;
            ChunkSizeBytes = chunkSizeBytes;
            NoncePrefix = noncePrefix;
        }

        public string JobId { get; }
        public int BundleIndex { get; }
        public int ChunkSizeBytes { get; }
        public byte[] NoncePrefix { get; }
    }
}
