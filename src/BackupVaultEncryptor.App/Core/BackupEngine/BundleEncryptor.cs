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
    /// Encrypts source files into bundle files using chunked AES-GCM.
    /// </summary>
    public class BundleEncryptor
    {
        private const string BundleFileExtension = ".benc";
        private readonly FileScanner _fileScanner;
        private readonly BundlePlanner _bundlePlanner;
        private readonly ManifestStorage _manifestStorage;
        private readonly VaultService _vaultService;
        private readonly AppLogger _logger;

        public BundleEncryptor(FileScanner fileScanner, BundlePlanner bundlePlanner, ManifestStorage manifestStorage, VaultService vaultService, AppLogger logger)
        {
            _fileScanner = fileScanner;
            _bundlePlanner = bundlePlanner;
            _manifestStorage = manifestStorage;
            _vaultService = vaultService;
            _logger = logger;
        }

        public async Task<BackupJobManifest> EncryptAsync(BackupJobEncryptRequest request, IProgress<BackupProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            var manifestPath = Path.Combine(request.DestinationRoot, $"{request.JobId}_manifest.json");

            BackupJobManifest manifest;
            if (File.Exists(manifestPath))
            {
                manifest = await _manifestStorage.LoadAsync(manifestPath, cancellationToken);
            }
            else
            {
                manifest = new BackupJobManifest
                {
                    JobId = request.JobId,
                    SourceRoot = request.SourceRoot,
                    CreatedUtc = DateTime.UtcNow,
                    BundleTargetSizeBytes = request.TargetBundleSizeBytes,
                    ChunkSizeBytes = request.ChunkSizeBytes,
                    Status = "InProgress"
                };
                await _manifestStorage.SaveAsync(manifest, manifestPath, cancellationToken);
            }

            var rootKeyRecord = _vaultService.GetOrCreateRootKey(request.UserSession, request.SourceRoot);
            var rootKey = _vaultService.UnwrapRootKey(rootKeyRecord, request.UserSession);

            try
            {
                IEnumerable<FileEntry> files;
                if (request.IncludedFullPaths is null)
                {
                    files = _fileScanner.ScanFiles(request.SourceRoot);
                }
                else
                {
                    files = _fileScanner.ScanFiles(request.SourceRoot, request.IncludedFullPaths);
                }
                var plannedBundles = _bundlePlanner.PlanBundles(files, request.TargetBundleSizeBytes);
                var bundleList = new List<PlannedBundle>(plannedBundles);

                var totalBytes = 0L;
                foreach (var plannedBundle in bundleList)
                {
                    totalBytes += plannedBundle.TotalPlaintextBytes;
                }

                var totalBundles = bundleList.Count;

                // When resuming, treat bundles that are already fully completed as processed so far.
                long processedBytes = 0;
                foreach (var plannedBundle in bundleList)
                {
                    if (IsBundleCompleted(manifest, request.DestinationRoot, plannedBundle))
                    {
                        processedBytes += plannedBundle.TotalPlaintextBytes;
                    }
                }

                var bundleIndex = 0;
                foreach (var plannedBundle in bundleList)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Skip bundles that are already present and recorded as completed.
                    if (IsBundleCompleted(manifest, request.DestinationRoot, plannedBundle))
                    {
                        bundleIndex++;
                        continue;
                    }

                    var currentBundleIndex = bundleIndex + 1;

                    // Report a snapshot before starting the bundle, if totals are known.
                    if (totalBytes > 0 && progress != null)
                    {
                        progress.Report(new BackupProgress
                        {
                            Phase = "Encrypting",
                            ProcessedBytes = processedBytes,
                            TotalBytes = totalBytes,
                            CurrentBundleIndex = currentBundleIndex,
                            TotalBundles = totalBundles,
                            CurrentItemName = $"bundle_{plannedBundle.BundleIndex:D4}"
                        });
                    }

                    var bundleInfo = await EncryptBundleAsync(
                        request,
                        rootKey,
                        plannedBundle,
                        totalBytes,
                        processedBytes,
                        currentBundleIndex,
                        totalBundles,
                        progress,
                        cancellationToken).ConfigureAwait(false);
                    manifest.Bundles.Add(bundleInfo);
                    await _manifestStorage.SaveAsync(manifest, manifestPath, cancellationToken).ConfigureAwait(false);

                    processedBytes += plannedBundle.TotalPlaintextBytes;
                    bundleIndex++;
                }

                manifest.Status = "Completed";
                await _manifestStorage.SaveAsync(manifest, manifestPath, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(rootKey);
            }

            return manifest;
        }

        private bool IsBundleCompleted(BackupJobManifest manifest, string destinationRoot, PlannedBundle plannedBundle)
        {
            foreach (var existing in manifest.Bundles)
            {
                if (existing.BundleIndex != plannedBundle.BundleIndex)
                {
                    continue;
                }

                var bundlePath = Path.Combine(destinationRoot, existing.BundleFileName);
                if (!File.Exists(bundlePath))
                {
                    return false;
                }

                var info = new FileInfo(bundlePath);
                if (info.Length != existing.BundleFileSizeBytes)
                {
                    return false;
                }

                return true;
            }

            return false;
        }

        private async Task<BackupBundleInfo> EncryptBundleAsync(
            BackupJobEncryptRequest request,
            byte[] rootKey,
            PlannedBundle plannedBundle,
            long totalBytes,
            long processedBytesBase,
            int currentBundleIndex,
            int totalBundles,
            IProgress<BackupProgress>? progress,
            CancellationToken cancellationToken)
        {
            var bundleKey = BundleKeyDerivation.DeriveBundleKey(rootKey, request.JobId, plannedBundle.BundleIndex);

            try
            {
                var bundleFileName = $"bundle_{plannedBundle.BundleIndex:D4}{BundleFileExtension}";
                var finalPath = Path.Combine(request.DestinationRoot, bundleFileName);
                var tempPath = finalPath + ".part";

                Directory.CreateDirectory(request.DestinationRoot);

                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }

                var bundleInfo = new BackupBundleInfo
                {
                    BundleIndex = plannedBundle.BundleIndex,
                    BundleFileName = bundleFileName,
                    TotalPlaintextBytes = plannedBundle.TotalPlaintextBytes
                };

                var noncePrefix = RandomNumberGenerator.GetBytes(8);

                await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    WriteHeader(stream, request.JobId, plannedBundle.BundleIndex, request.ChunkSizeBytes, noncePrefix);

                    using var aesGcm = new AesGcm(bundleKey, 16);
                    var chunkBuffer = new byte[request.ChunkSizeBytes];
                    var ciphertextBuffer = new byte[request.ChunkSizeBytes];
                    var tagBuffer = new byte[16];
                    var chunkCounter = 0;

                    long bundleProcessed = 0;

                    foreach (var entry in plannedBundle.Entries)
                    {
                        var manifestEntry = new BackupBundleEntry
                        {
                            RelativePath = entry.File.RelativePath,
                            OriginalSizeBytes = entry.File.Length,
                            BundleIndex = plannedBundle.BundleIndex,
                            PlaintextOffset = entry.PlaintextOffset,
                            PlaintextLength = entry.PlaintextLength
                        };

                        bundleInfo.Entries.Add(manifestEntry);

                        using var fileStream = new FileStream(entry.File.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                        int bytesRead;
                        while ((bytesRead = await fileStream.ReadAsync(chunkBuffer, 0, chunkBuffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            var nonce = BuildNonce(noncePrefix, chunkCounter);
                            Array.Clear(ciphertextBuffer, 0, ciphertextBuffer.Length);
                            Array.Clear(tagBuffer, 0, tagBuffer.Length);

                            aesGcm.Encrypt(nonce, chunkBuffer.AsSpan(0, bytesRead), ciphertextBuffer.AsSpan(0, bytesRead), tagBuffer);

                            // Write per-chunk layout: PlaintextLength, Ciphertext, Tag
                            var lengthBytes = BitConverter.GetBytes(bytesRead);
                            await stream.WriteAsync(lengthBytes, 0, lengthBytes.Length, cancellationToken).ConfigureAwait(false);
                            await stream.WriteAsync(ciphertextBuffer, 0, bytesRead, cancellationToken).ConfigureAwait(false);
                            await stream.WriteAsync(tagBuffer, 0, tagBuffer.Length, cancellationToken).ConfigureAwait(false);

                            bundleProcessed += bytesRead;

                            if (totalBytes > 0 && progress != null)
                            {
                                var currentProcessed = processedBytesBase + bundleProcessed;

                                progress.Report(new BackupProgress
                                {
                                    Phase = "Encrypting",
                                    ProcessedBytes = currentProcessed,
                                    TotalBytes = totalBytes,
                                    CurrentBundleIndex = currentBundleIndex,
                                    TotalBundles = totalBundles,
                                    CurrentItemName = entry.File.RelativePath
                                });
                            }

                            chunkCounter++;
                        }
                    }
                }

                var info = new FileInfo(tempPath);
                bundleInfo.BundleFileSizeBytes = info.Length;

                if (File.Exists(finalPath))
                {
                    File.Delete(finalPath);
                }

                File.Move(tempPath, finalPath);

                _logger.Log($"Created bundle {bundleFileName} ({bundleInfo.BundleFileSizeBytes} bytes) for job {request.JobId}.");

                return bundleInfo;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bundleKey);
            }
        }

        private static void WriteHeader(Stream stream, string jobId, int bundleIndex, int chunkSizeBytes, byte[] noncePrefix)
        {
            // Magic "BVEB"
            stream.Write(new byte[] { (byte)'B', (byte)'V', (byte)'E', (byte)'B' }, 0, 4);

            // Version 0x0001
            stream.Write(BitConverter.GetBytes((short)1), 0, 2);

            // Reserved/flags (2 bytes, zero)
            stream.Write(new byte[2], 0, 2);

            // JobId as GUID bytes
            var jobGuid = Guid.Parse(jobId);
            var jobBytes = jobGuid.ToByteArray();
            stream.Write(jobBytes, 0, jobBytes.Length);

            // BundleIndex (4-byte LE)
            stream.Write(BitConverter.GetBytes(bundleIndex), 0, 4);

            // ChunkSizeBytes (4-byte LE)
            stream.Write(BitConverter.GetBytes(chunkSizeBytes), 0, 4);

            // BundleNoncePrefix (8 bytes)
            stream.Write(noncePrefix, 0, noncePrefix.Length);
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

    public class BackupJobEncryptRequest
    {
        public string JobId { get; set; } = Guid.NewGuid().ToString();
        public string SourceRoot { get; set; } = string.Empty;
        public string DestinationRoot { get; set; } = string.Empty;
        public long TargetBundleSizeBytes { get; set; }
        public int ChunkSizeBytes { get; set; }
        public UserSession UserSession { get; set; } = null!;
        /// <summary>
        /// Optional list of full file paths to include. When null, all files
        /// under SourceRoot are included. Used to support single-file sources
        /// without changing the overall engine design.
        /// </summary>
        public IEnumerable<string>? IncludedFullPaths { get; set; }
    }
}
