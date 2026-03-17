using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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

            var runStopwatch = Stopwatch.StartNew();
            var runMetrics = new EncryptRunMetrics();

            var scratchJobDirectory = GetScratchJobDirectory(request.ScratchRoot, request.JobId);
            var localManifestPath = Path.Combine(scratchJobDirectory, $"{request.JobId}_manifest.json");
            var destinationManifestPath = Path.Combine(request.DestinationRoot, $"{request.JobId}_manifest.json");

            Directory.CreateDirectory(scratchJobDirectory);

            var manifest = await LoadOrCreateManifestAsync(
                request,
                localManifestPath,
                destinationManifestPath,
                cancellationToken).ConfigureAwait(false);

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

                long processedBytes = 0;
                var bundleIndex = 0;
                foreach (var plannedBundle in plannedBundles)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!runMetrics.FirstBundleReadyLogged)
                    {
                        runMetrics.FirstBundleReadyLogged = true;
                        EmitMetricsLine(
                            progress,
                            $"First bundle ready in {FormatDuration(runStopwatch.Elapsed)} ({plannedBundle.Entries.Count} files, {FormatMegabytes(plannedBundle.TotalPlaintextBytes)} MB).",
                            bundleIndex + 1,
                            0);
                    }

                    var existingBundle = FindBundle(manifest, plannedBundle.BundleIndex);
                    var reusedBundleMetrics = await TryHandleExistingBundleAsync(
                        request,
                        manifest,
                        existingBundle,
                        plannedBundle,
                        scratchJobDirectory,
                        localManifestPath,
                        runStopwatch,
                        runMetrics,
                        progress,
                        processedBytes,
                        bundleIndex + 1,
                        cancellationToken).ConfigureAwait(false);
                    if (reusedBundleMetrics != null)
                    {
                        runMetrics.AddBundle(reusedBundleMetrics);
                        processedBytes += plannedBundle.TotalPlaintextBytes;
                        bundleIndex++;
                        continue;
                    }

                    var currentBundleIndex = bundleIndex + 1;

                    var bundleResult = await EncryptBundleAsync(
                        request,
                        rootKey,
                        plannedBundle,
                        scratchJobDirectory,
                        processedBytes,
                        currentBundleIndex,
                        progress,
                        cancellationToken).ConfigureAwait(false);

                    var bundleInfo = bundleResult.BundleInfo;

                    ReplaceBundle(manifest, bundleInfo);
                    await _manifestStorage.SaveAsync(manifest, localManifestPath, cancellationToken).ConfigureAwait(false);
                    bundleResult.Metrics.PublishElapsed = await PublishBundleToDestinationAsync(request, scratchJobDirectory, bundleInfo, cancellationToken).ConfigureAwait(false);
                    bundleInfo.IsPublished = true;
                    await _manifestStorage.SaveAsync(manifest, localManifestPath, cancellationToken).ConfigureAwait(false);

                    if (!runMetrics.FirstBundlePublishedLogged)
                    {
                        runMetrics.FirstBundlePublishedLogged = true;
                        EmitMetricsLine(
                            progress,
                            $"First bundle published in {FormatDuration(runStopwatch.Elapsed)}.",
                            currentBundleIndex,
                            processedBytes + plannedBundle.TotalPlaintextBytes);
                    }

                    runMetrics.AddBundle(bundleResult.Metrics);
                    EmitMetricsLine(
                        progress,
                        $"Bundle {currentBundleIndex}: {bundleResult.Metrics.FileCount} files, {FormatMegabytes(bundleResult.Metrics.PlaintextBytes)} MB | read {FormatDuration(bundleResult.Metrics.SourceReadElapsed)} | encrypt/write {FormatDuration(bundleResult.Metrics.EncryptWriteElapsed)} | publish {FormatDuration(bundleResult.Metrics.PublishElapsed)}.",
                        currentBundleIndex,
                        processedBytes + plannedBundle.TotalPlaintextBytes);

                    processedBytes += plannedBundle.TotalPlaintextBytes;
                    bundleIndex++;
                }

                manifest.Status = "Completed";
                await _manifestStorage.SaveAsync(manifest, localManifestPath, cancellationToken).ConfigureAwait(false);
                await PublishManifestToDestinationAsync(manifest, destinationManifestPath, cancellationToken).ConfigureAwait(false);

                var elapsedSeconds = Math.Max(runStopwatch.Elapsed.TotalSeconds, 0.001d);
                var filesPerSecond = runMetrics.TotalFiles / elapsedSeconds;
                var megabytesPerSecond = (runMetrics.TotalBytes / 1024d / 1024d) / elapsedSeconds;
                EmitMetricsLine(
                    progress,
                    $"Encrypt summary: {runMetrics.TotalFiles} files, {FormatMegabytes(runMetrics.TotalBytes)} MB in {FormatDuration(runStopwatch.Elapsed)} | {filesPerSecond:0.0} files/s | {megabytesPerSecond:0.0} MB/s | read {FormatDuration(runMetrics.TotalSourceReadElapsed)} | encrypt/write {FormatDuration(runMetrics.TotalEncryptWriteElapsed)} | publish {FormatDuration(runMetrics.TotalPublishElapsed)}.",
                    bundleIndex,
                    processedBytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(rootKey);
            }

            return manifest;
        }

        private static string GetScratchJobDirectory(string scratchRoot, string jobId)
        {
            return Path.Combine(scratchRoot, "jobs", jobId);
        }

        private async Task<BackupJobManifest> LoadOrCreateManifestAsync(
            BackupJobEncryptRequest request,
            string localManifestPath,
            string destinationManifestPath,
            CancellationToken cancellationToken)
        {
            if (File.Exists(localManifestPath))
            {
                return await _manifestStorage.LoadAsync(localManifestPath, cancellationToken).ConfigureAwait(false);
            }

            if (File.Exists(destinationManifestPath))
            {
                var manifest = await _manifestStorage.LoadAsync(destinationManifestPath, cancellationToken).ConfigureAwait(false);
                await _manifestStorage.SaveAsync(manifest, localManifestPath, cancellationToken).ConfigureAwait(false);
                return manifest;
            }

            var newManifest = new BackupJobManifest
            {
                JobId = request.JobId,
                SourceRoot = request.SourceRoot,
                CreatedUtc = DateTime.UtcNow,
                BundleTargetSizeBytes = request.TargetBundleSizeBytes,
                ChunkSizeBytes = request.ChunkSizeBytes,
                Status = "InProgress"
            };

            await _manifestStorage.SaveAsync(newManifest, localManifestPath, cancellationToken).ConfigureAwait(false);
            return newManifest;
        }

        private static BackupBundleInfo? FindBundle(BackupJobManifest manifest, int bundleIndex)
        {
            return manifest.Bundles.FirstOrDefault(bundle => bundle.BundleIndex == bundleIndex);
        }

        private static void ReplaceBundle(BackupJobManifest manifest, BackupBundleInfo bundleInfo)
        {
            manifest.Bundles.RemoveAll(bundle => bundle.BundleIndex == bundleInfo.BundleIndex);
            manifest.Bundles.Add(bundleInfo);
            manifest.Bundles.Sort((left, right) => left.BundleIndex.CompareTo(right.BundleIndex));
        }

        private async Task<BundleMetrics?> TryHandleExistingBundleAsync(
            BackupJobEncryptRequest request,
            BackupJobManifest manifest,
            BackupBundleInfo? existingBundle,
            PlannedBundle plannedBundle,
            string scratchJobDirectory,
            string localManifestPath,
            Stopwatch runStopwatch,
            EncryptRunMetrics runMetrics,
            IProgress<BackupProgress>? progress,
            long processedBytesBase,
            int currentBundleIndex,
            CancellationToken cancellationToken)
        {
            if (existingBundle == null)
            {
                return null;
            }

            existingBundle.TotalPlaintextBytes = plannedBundle.TotalPlaintextBytes;

            var localBundlePath = Path.Combine(scratchJobDirectory, existingBundle.BundleFileName);
            var destinationBundlePath = Path.Combine(request.DestinationRoot, existingBundle.BundleFileName);
            var hasValidLocalBundle = IsBundleFileValid(localBundlePath, existingBundle.BundleFileSizeBytes);
            var hasValidPublishedBundle = IsBundleFileValid(destinationBundlePath, existingBundle.BundleFileSizeBytes);

            if (existingBundle.IsPublished && hasValidPublishedBundle)
            {
                ReportBundleState(progress, processedBytesBase, currentBundleIndex, plannedBundle.BundleIndex, "Encrypting");
                if (!runMetrics.FirstBundlePublishedLogged)
                {
                    runMetrics.FirstBundlePublishedLogged = true;
                    EmitMetricsLine(
                        progress,
                        $"First bundle published in {FormatDuration(runStopwatch.Elapsed)}.",
                        currentBundleIndex,
                        processedBytesBase + plannedBundle.TotalPlaintextBytes);
                }

                var metrics = BundleMetrics.ForBundle(plannedBundle);
                EmitMetricsLine(
                    progress,
                    $"Bundle {currentBundleIndex}: {metrics.FileCount} files, {FormatMegabytes(metrics.PlaintextBytes)} MB | reused already-published bundle.",
                    currentBundleIndex,
                    processedBytesBase + plannedBundle.TotalPlaintextBytes);
                return metrics;
            }

            if (hasValidLocalBundle)
            {
                var publishElapsed = await PublishBundleToDestinationAsync(request, scratchJobDirectory, existingBundle, cancellationToken).ConfigureAwait(false);
                existingBundle.IsPublished = true;
                await _manifestStorage.SaveAsync(manifest, localManifestPath, cancellationToken).ConfigureAwait(false);
                ReportBundleState(progress, processedBytesBase, currentBundleIndex, plannedBundle.BundleIndex, "Publishing");

                if (!runMetrics.FirstBundlePublishedLogged)
                {
                    runMetrics.FirstBundlePublishedLogged = true;
                    EmitMetricsLine(
                        progress,
                        $"First bundle published in {FormatDuration(runStopwatch.Elapsed)}.",
                        currentBundleIndex,
                        processedBytesBase + plannedBundle.TotalPlaintextBytes);
                }

                var metrics = BundleMetrics.ForBundle(plannedBundle);
                metrics.PublishElapsed = publishElapsed;
                EmitMetricsLine(
                    progress,
                    $"Bundle {currentBundleIndex}: {metrics.FileCount} files, {FormatMegabytes(metrics.PlaintextBytes)} MB | reused local scratch bundle | publish {FormatDuration(metrics.PublishElapsed)}.",
                    currentBundleIndex,
                    processedBytesBase + plannedBundle.TotalPlaintextBytes);
                return metrics;
            }

            existingBundle.IsPublished = false;
            manifest.Bundles.RemoveAll(bundle => bundle.BundleIndex == plannedBundle.BundleIndex);
            await _manifestStorage.SaveAsync(manifest, localManifestPath, cancellationToken).ConfigureAwait(false);
            return null;
        }

        private static bool IsBundleFileValid(string path, long expectedLength)
        {
            if (!File.Exists(path))
            {
                return false;
            }

            var info = new FileInfo(path);
            return info.Length == expectedLength;
        }

        private async Task<EncryptBundleResult> EncryptBundleAsync(
            BackupJobEncryptRequest request,
            byte[] rootKey,
            PlannedBundle plannedBundle,
            string scratchJobDirectory,
            long processedBytesBase,
            int currentBundleIndex,
            IProgress<BackupProgress>? progress,
            CancellationToken cancellationToken)
        {
            var bundleKey = BundleKeyDerivation.DeriveBundleKey(rootKey, request.JobId, plannedBundle.BundleIndex);
            var metrics = BundleMetrics.ForBundle(plannedBundle);

            try
            {
                var bundleFileName = $"bundle_{plannedBundle.BundleIndex:D4}{BundleFileExtension}";
                var finalPath = Path.Combine(scratchJobDirectory, bundleFileName);
                var tempPath = finalPath + ".part";

                Directory.CreateDirectory(scratchJobDirectory);

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
                        while (true)
                        {
                            var readStarted = Stopwatch.GetTimestamp();
                            var bytesRead = await fileStream.ReadAsync(chunkBuffer, 0, chunkBuffer.Length, cancellationToken).ConfigureAwait(false);
                            metrics.SourceReadElapsed += GetElapsed(readStarted);

                            if (bytesRead <= 0)
                            {
                                break;
                            }

                            cancellationToken.ThrowIfCancellationRequested();

                            var encryptWriteStarted = Stopwatch.GetTimestamp();
                            var nonce = BuildNonce(noncePrefix, chunkCounter);
                            Array.Clear(ciphertextBuffer, 0, ciphertextBuffer.Length);
                            Array.Clear(tagBuffer, 0, tagBuffer.Length);

                            aesGcm.Encrypt(nonce, chunkBuffer.AsSpan(0, bytesRead), ciphertextBuffer.AsSpan(0, bytesRead), tagBuffer);

                            // Write per-chunk layout: PlaintextLength, Ciphertext, Tag
                            var lengthBytes = BitConverter.GetBytes(bytesRead);
                            await stream.WriteAsync(lengthBytes, 0, lengthBytes.Length, cancellationToken).ConfigureAwait(false);
                            await stream.WriteAsync(ciphertextBuffer, 0, bytesRead, cancellationToken).ConfigureAwait(false);
                            await stream.WriteAsync(tagBuffer, 0, tagBuffer.Length, cancellationToken).ConfigureAwait(false);
                            metrics.EncryptWriteElapsed += GetElapsed(encryptWriteStarted);

                            bundleProcessed += bytesRead;

                            if (progress != null)
                            {
                                var currentProcessed = processedBytesBase + bundleProcessed;

                                progress.Report(new BackupProgress
                                {
                                    Phase = "Encrypting",
                                    ProcessedBytes = currentProcessed,
                                    TotalBytes = 0,
                                    CurrentBundleIndex = currentBundleIndex,
                                    TotalBundles = 0,
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

                return new EncryptBundleResult(bundleInfo, metrics);
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

        private async Task<TimeSpan> PublishBundleToDestinationAsync(
            BackupJobEncryptRequest request,
            string scratchJobDirectory,
            BackupBundleInfo bundleInfo,
            CancellationToken cancellationToken)
        {
            var started = Stopwatch.GetTimestamp();
            Directory.CreateDirectory(request.DestinationRoot);

            var sourcePath = Path.Combine(scratchJobDirectory, bundleInfo.BundleFileName);
            var destinationPath = Path.Combine(request.DestinationRoot, bundleInfo.BundleFileName);
            var tempDestinationPath = destinationPath + ".part";

            if (File.Exists(tempDestinationPath))
            {
                File.Delete(tempDestinationPath);
            }

            await using (var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            await using (var destinationStream = new FileStream(tempDestinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await sourceStream.CopyToAsync(destinationStream, 81920, cancellationToken).ConfigureAwait(false);
            }

            if (File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }

            File.Move(tempDestinationPath, destinationPath);
            _logger.Log($"Published bundle {bundleInfo.BundleFileName} for job {request.JobId}.");
            return GetElapsed(started);
        }

        private async Task PublishManifestToDestinationAsync(BackupJobManifest manifest, string destinationManifestPath, CancellationToken cancellationToken)
        {
            var destinationDirectory = Path.GetDirectoryName(destinationManifestPath);
            if (!string.IsNullOrEmpty(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            await _manifestStorage.SaveAsync(manifest, destinationManifestPath, cancellationToken).ConfigureAwait(false);
            _logger.Log($"Published final manifest for job {manifest.JobId}.");
        }

        private static void ReportBundleState(IProgress<BackupProgress>? progress, long processedBytesBase, int currentBundleIndex, int bundleIndex, string phase)
        {
            if (progress == null)
            {
                return;
            }

            progress.Report(new BackupProgress
            {
                Phase = phase,
                ProcessedBytes = processedBytesBase,
                TotalBytes = 0,
                CurrentBundleIndex = currentBundleIndex,
                TotalBundles = 0,
                CurrentItemName = $"bundle_{bundleIndex:D4}"
            });
        }

        private void EmitMetricsLine(IProgress<BackupProgress>? progress, string message, int currentBundleIndex, long processedBytes)
        {
            _logger.Log(message);

            if (progress == null)
            {
                return;
            }

            progress.Report(new BackupProgress
            {
                Phase = "Metrics",
                ProcessedBytes = processedBytes,
                TotalBytes = 0,
                CurrentBundleIndex = currentBundleIndex,
                TotalBundles = 0,
                CurrentItemName = message
            });
        }

        private static TimeSpan GetElapsed(long startedTimestamp)
        {
            var elapsedTicks = Stopwatch.GetTimestamp() - startedTimestamp;
            return TimeSpan.FromSeconds(elapsedTicks / (double)Stopwatch.Frequency);
        }

        private static string FormatDuration(TimeSpan duration)
        {
            return duration.TotalSeconds >= 10
                ? duration.ToString(@"hh\:mm\:ss")
                : $"{duration.TotalSeconds:0.00} s";
        }

        private static string FormatMegabytes(long bytes)
        {
            return (bytes / 1024d / 1024d).ToString("0.0");
        }

        private sealed class EncryptRunMetrics
        {
            public bool FirstBundleReadyLogged { get; set; }
            public bool FirstBundlePublishedLogged { get; set; }
            public long TotalFiles { get; private set; }
            public long TotalBytes { get; private set; }
            public TimeSpan TotalSourceReadElapsed { get; private set; }
            public TimeSpan TotalEncryptWriteElapsed { get; private set; }
            public TimeSpan TotalPublishElapsed { get; private set; }

            public void AddBundle(BundleMetrics metrics)
            {
                TotalFiles += metrics.FileCount;
                TotalBytes += metrics.PlaintextBytes;
                TotalSourceReadElapsed += metrics.SourceReadElapsed;
                TotalEncryptWriteElapsed += metrics.EncryptWriteElapsed;
                TotalPublishElapsed += metrics.PublishElapsed;
            }
        }

        private sealed class EncryptBundleResult
        {
            public EncryptBundleResult(BackupBundleInfo bundleInfo, BundleMetrics metrics)
            {
                BundleInfo = bundleInfo;
                Metrics = metrics;
            }

            public BackupBundleInfo BundleInfo { get; }
            public BundleMetrics Metrics { get; }
        }

        private sealed class BundleMetrics
        {
            public int FileCount { get; set; }
            public long PlaintextBytes { get; set; }
            public TimeSpan SourceReadElapsed { get; set; }
            public TimeSpan EncryptWriteElapsed { get; set; }
            public TimeSpan PublishElapsed { get; set; }

            public static BundleMetrics ForBundle(PlannedBundle plannedBundle)
            {
                return new BundleMetrics
                {
                    FileCount = plannedBundle.Entries.Count,
                    PlaintextBytes = plannedBundle.TotalPlaintextBytes
                };
            }
        }
    }

    public class BackupJobEncryptRequest
    {
        public string JobId { get; set; } = Guid.NewGuid().ToString();
        public string SourceRoot { get; set; } = string.Empty;
        public string DestinationRoot { get; set; } = string.Empty;
        public string ScratchRoot { get; set; } = string.Empty;
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
