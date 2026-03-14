using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using BackupVaultEncryptor.App.Core.BackupEngine;
using BackupVaultEncryptor.App.Core;
using BackupVaultEncryptor.App.Infrastructure;
using BackupVaultEncryptor.App.Logging;
using Microsoft.Win32;
using BackupVaultEncryptor.App.UI;

namespace BackupVaultEncryptor.App;

/// <summary>
/// Main window for running encrypt/decrypt jobs and managing basic settings.
/// Code-behind is kept simple and explicit for readability.
/// </summary>
public partial class MainWindow : Window
{
    private readonly AppState _appState;
    private readonly BundleEncryptor _encryptor;
    private readonly BundleDecryptor _decryptor;
    private readonly AppLogger _logger;
    private readonly UserSettingsStore _settingsStore;

    private bool _isJobRunning;
    private CancellationTokenSource? _currentJobCts;
    private string? _lastProgressPhase;

    private const int MainLogMaxLines = 200;

    public MainWindow(AppState appState, BundleEncryptor encryptor, BundleDecryptor decryptor, AppLogger logger, UserSettingsStore settingsStore)
    {
        _appState = appState;
        _encryptor = encryptor;
        _decryptor = decryptor;
        _logger = logger;
        _settingsStore = settingsStore;

        InitializeComponent();

        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Populate settings tab from current settings (bytes -> GiB/MiB)
        var settings = _appState.CurrentSettings;
        var bundleGiB = settings.BundleTargetSizeBytes / (1024d * 1024d * 1024d);
        var chunkMiB = settings.ChunkSizeBytes / (1024d * 1024d);

        BundleSizeGiBTextBox.Text = bundleGiB.ToString("0.##", CultureInfo.InvariantCulture);
        ChunkSizeMiBTextBox.Text = chunkMiB.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private void EncryptBrowseSourceButton_Click(object sender, RoutedEventArgs e)
    {
        // Show a tiny choice menu for Folder vs File when selecting the encrypt source.
        if (sender is System.Windows.Controls.Button button && button.ContextMenu != null)
        {
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.IsOpen = true;
        }
    }

    private void EncryptSourceFolderMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog();
        var result = dialog.ShowDialog();
        if (result == true && !string.IsNullOrEmpty(dialog.FolderName))
        {
            SourceTextBox.Text = dialog.FolderName;
        }
    }

    private void EncryptSourceFileMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog();
        dialog.Title = "Select file to encrypt";

        var result = dialog.ShowDialog();
        if (result == true && !string.IsNullOrEmpty(dialog.FileName))
        {
            SourceTextBox.Text = dialog.FileName;
        }
    }

    private void EncryptBrowseDestinationButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog();
        var result = dialog.ShowDialog();
        if (result == true && !string.IsNullOrEmpty(dialog.FolderName))
        {
            DestinationTextBox.Text = dialog.FolderName;
        }
    }

    private void DecryptBrowseSourceButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select manifest file",
            Filter = "Manifest files (*.json)|*.json|All files (*.*)|*.*"
        };

        var result = dialog.ShowDialog();
        if (result == true && !string.IsNullOrEmpty(dialog.FileName))
        {
            DecryptSourceTextBox.Text = dialog.FileName;
        }
    }

    private void DecryptBrowseDestinationButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog();
        var result = dialog.ShowDialog();
        if (result == true && !string.IsNullOrEmpty(dialog.FolderName))
        {
            DecryptDestinationTextBox.Text = dialog.FolderName;
        }
    }

    private async void EncryptStartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isJobRunning)
        {
            MessageBox.Show(this, "Another job is already running. Please wait or cancel it first.", "Job running", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_appState.CurrentSession == null)
        {
            MessageBox.Show(this, "No active session. Please restart the application and log in.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var source = SourceTextBox.Text?.Trim() ?? string.Empty;
        var destination = DestinationTextBox.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(destination))
        {
            MessageBox.Show(this, "Source and destination paths are required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Encrypt mode: source can be folder or single file; destination is job output folder.
        bool isFolder = Directory.Exists(source);
        bool isFile = File.Exists(source);

        if (!isFolder && !isFile)
        {
            MessageBox.Show(this, "Source path does not exist.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var destinationExistedBefore = Directory.Exists(destination);
        try
        {
            Directory.CreateDirectory(destination);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to create destination folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var destinationCreatedByApp = !destinationExistedBefore;

        await RunEncryptAsync(source, destination, isFolder, isFile, destinationCreatedByApp);
    }

    private async void DecryptStartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isJobRunning)
        {
            MessageBox.Show(this, "Another job is already running. Please wait or cancel it first.", "Job running", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_appState.CurrentSession == null)
        {
            MessageBox.Show(this, "No active session. Please restart the application and log in.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var source = DecryptSourceTextBox.Text?.Trim() ?? string.Empty;
        var destination = DecryptDestinationTextBox.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(destination))
        {
            MessageBox.Show(this, "Manifest and destination paths are required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Decrypt mode: source is manifest file, destination is folder.
        if (!File.Exists(source))
        {
            MessageBox.Show(this, "Manifest file does not exist.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Directory.CreateDirectory(destination);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to create destination folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        await RunDecryptAsync(source, destination);
    }

    private async Task RunEncryptAsync(string sourcePath, string destinationRoot, bool isFolder, bool isFile, bool destinationCreatedByApp)
    {
        var settings = _appState.CurrentSettings;

        if (settings.BundleTargetSizeBytes <= 0 || settings.ChunkSizeBytes <= 0)
        {
            MessageBox.Show(this, "Bundle size and chunk size must be greater than zero.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string sourceRoot;
        string[]? includedFiles = null;

        if (isFolder)
        {
            sourceRoot = sourcePath;
        }
        else if (isFile)
        {
            sourceRoot = Path.GetDirectoryName(sourcePath) ?? sourcePath;
            includedFiles = new[] { sourcePath };
        }
        else
        {
            MessageBox.Show(this, "Source must be an existing file or folder.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var jobId = Guid.NewGuid().ToString();
        var jobOutputRoot = destinationRoot;

        _currentJobCts?.Dispose();
        _currentJobCts = new CancellationTokenSource();

        SetJobRunningState(true, "Scanning and planning...");
        AppendMainLogLine($"[Encrypt] Job {jobId} started. Source: {sourcePath}, Destination: {destinationRoot}");

        try
        {
            var request = new BackupJobEncryptRequest
            {
                JobId = jobId,
                SourceRoot = sourceRoot,
                DestinationRoot = jobOutputRoot,
                TargetBundleSizeBytes = settings.BundleTargetSizeBytes,
                ChunkSizeBytes = settings.ChunkSizeBytes,
                UserSession = _appState.CurrentSession!,
                IncludedFullPaths = includedFiles
            };

            var progress = new Progress<BackupProgress>(UpdateProgress);
            var ct = _currentJobCts?.Token ?? CancellationToken.None;

            await _encryptor.EncryptAsync(request, progress, ct);

            StatusTextBlock.Text = "Encryption completed successfully.";
            AppendMainLogLine($"[Encrypt] Job {jobId} completed successfully.");
        }
        catch (OperationCanceledException)
        {
            StatusTextBlock.Text = "Encryption canceled by user.";
            _logger.Log($"Encryption canceled by user. Job {jobId}.");
            AppendMainLogLine($"[Encrypt] Job {jobId} canceled by user.");

            // Best-effort cleanup of artifacts for this canceled encrypt job only.
            await CleanupCanceledEncryptJobAsync(jobId, jobOutputRoot, destinationCreatedByApp);
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Encryption failed: {ex.Message}";
            _logger.Log($"Encryption failed: {ex}");
            AppendMainLogLine($"[Encrypt] Job {jobId} failed: {ex.Message}");
            MessageBox.Show(this, "Encryption failed. See log output for details.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetJobRunningState(false, null);
        }
    }

    /// <summary>
    /// Best-effort cleanup for a canceled encrypt job.
    ///
    /// This deletes only artifacts that belong to the given job:
    /// - The manifest file: {jobId}_manifest.json
    /// - All completed bundle files listed in the manifest and their matching .part files
    /// - A single inferred current temp bundle file: bundle_{N:D4}.benc.part
    ///
    /// It deletes the destination folder itself only if it was created by this
    /// encrypt run and is empty after artifact cleanup, and does not use
    /// wildcards across the whole destination. Failures are logged but do not
    /// turn cancel into an error path.
    /// </summary>
    private async Task CleanupCanceledEncryptJobAsync(string jobId, string destinationRoot, bool destinationCreatedByApp)
    {
        var manifestPath = Path.Combine(destinationRoot, $"{jobId}_manifest.json");
        var manifestStorage = new ManifestStorage();

        BackupVaultEncryptor.App.Core.Models.BackupJobManifest? manifest = null;
        var manifestLoaded = false;
        var completedBundles = 0;

        // Try to load the manifest if it exists. If loading fails, we still
        // clean up what we can without throwing.
        try
        {
            if (File.Exists(manifestPath))
            {
                manifest = await manifestStorage.LoadAsync(manifestPath, CancellationToken.None);
                if (manifest != null)
                {
                    manifestLoaded = true;
                    completedBundles = manifest.Bundles.Count;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Log($"Cleanup: failed to load manifest for canceled job {jobId}: {ex}");
            manifestLoaded = false;
            completedBundles = 0;
        }

        if (manifestLoaded && manifest != null)
        {
            foreach (var bundle in manifest.Bundles)
            {
                var bundlePath = Path.Combine(destinationRoot, bundle.BundleFileName);
                var bundlePartPath = bundlePath + ".part";

                TryDeleteFile(bundlePath, $"bundle file {bundle.BundleFileName} for canceled job {jobId}");
                TryDeleteFile(bundlePartPath, $"bundle temp file {Path.GetFileName(bundlePartPath)} for canceled job {jobId}");
            }
        }

        // Also delete the current in-progress temp bundle file if we can
        // infer its name safely from the number of completed bundles.
        // If the manifest could not be loaded, completedBundles will be 0
        // and we will only touch bundle_0000.benc.part.
        var inferredTempFileName = $"bundle_{completedBundles:D4}.benc.part";
        var inferredTempPath = Path.Combine(destinationRoot, inferredTempFileName);
        TryDeleteFile(inferredTempPath, $"inferred current temp bundle {inferredTempFileName} for canceled job {jobId}");

        // Finally, delete the manifest file itself for this job.
        TryDeleteFile(manifestPath, $"manifest for canceled job {jobId}");

        // Optionally delete the destination folder if we know it was created
        // by this encrypt run and it is now empty after artifact cleanup.
        if (destinationCreatedByApp)
        {
            try
            {
                if (Directory.Exists(destinationRoot))
                {
                    var hasEntries = Directory.EnumerateFileSystemEntries(destinationRoot).Any();
                    if (!hasEntries)
                    {
                        try
                        {
                            Directory.Delete(destinationRoot);
                            _logger.Log($"Cleanup: deleted empty destination folder '{destinationRoot}' for canceled job {jobId} (created by app).");
                            AppendMainLogLine($"[Encrypt] Deleted empty destination folder after cleanup for canceled job {jobId}.");
                        }
                        catch (Exception ex)
                        {
                            _logger.Log($"Cleanup: failed to delete empty destination folder '{destinationRoot}' for canceled job {jobId}: {ex.Message}");
                            AppendMainLogLine("[Encrypt] Cleanup could not delete empty destination folder. See log for details.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Log($"Cleanup: failed while inspecting destination folder '{destinationRoot}' for canceled job {jobId}: {ex.Message}");
            }
        }

        _logger.Log($"Cleanup completed for canceled job {jobId} in {destinationRoot}. ManifestLoaded={manifestLoaded}, CompletedBundles={completedBundles}.");
        AppendMainLogLine($"[Encrypt] Cleanup attempted for canceled job {jobId}.");
    }

    /// <summary>
    /// Helper that deletes a single file if it exists.
    /// Any failure is logged and surfaced as a brief main-log note, but
    /// exceptions are swallowed so that cleanup remains best-effort.
    /// </summary>
    private void TryDeleteFile(string path, string description)
    {
        try
        {
            if (!File.Exists(path))
            {
                return;
            }

            File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.Log($"Cleanup: failed to delete {description} at '{path}': {ex.Message}");
            AppendMainLogLine($"[Encrypt] Cleanup could not delete {description}. See log for details.");
        }
    }

    private async Task RunDecryptAsync(string manifestPath, string destinationRoot)
    {
        _currentJobCts?.Dispose();
        _currentJobCts = new CancellationTokenSource();

        SetJobRunningState(true, "Decrypting...");
        AppendMainLogLine($"[Decrypt] Started. Manifest: {manifestPath}, Destination: {destinationRoot}");

        try
        {
            var request = new BackupJobDecryptRequest
            {
                ManifestPath = manifestPath,
                DestinationRoot = destinationRoot,
                UserSession = _appState.CurrentSession!
            };

            var progress = new Progress<BackupProgress>(UpdateProgress);
            var ct = _currentJobCts?.Token ?? CancellationToken.None;

            await _decryptor.DecryptAsync(request, progress, ct);

            StatusTextBlock.Text = "Decryption completed successfully.";
            AppendMainLogLine("[Decrypt] Completed successfully.");
        }
        catch (OperationCanceledException)
        {
            StatusTextBlock.Text = "Decryption canceled by user.";
            _logger.Log("Decryption canceled by user.");
            AppendMainLogLine("[Decrypt] Canceled by user.");
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Decryption failed: {ex.Message}";
            _logger.Log($"Decryption failed: {ex}");
            AppendMainLogLine($"[Decrypt] Failed: {ex.Message}");
            MessageBox.Show(this, "Decryption failed. See log output for details.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetJobRunningState(false, null);
        }
    }

    private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryParseSettingsFromUi(out var bundleBytes, out var chunkBytes))
        {
            return;
        }

        _appState.CurrentSettings.BundleTargetSizeBytes = bundleBytes;
        _appState.CurrentSettings.ChunkSizeBytes = chunkBytes;
        _settingsStore.Save(_appState.CurrentSettings);

        MessageBox.Show(this, "Settings saved.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private bool TryParseSettingsFromUi(out long bundleBytes, out int chunkBytes)
    {
        bundleBytes = 0;
        chunkBytes = 0;

        if (!double.TryParse(BundleSizeGiBTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var bundleGiB) || bundleGiB <= 0)
        {
            MessageBox.Show(this, "Bundle size (GiB) must be a positive number.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!double.TryParse(ChunkSizeMiBTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var chunkMiB) || chunkMiB <= 0)
        {
            MessageBox.Show(this, "Chunk size (MiB) must be a positive number.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        bundleBytes = (long)(bundleGiB * 1024 * 1024 * 1024);
        chunkBytes = (int)(chunkMiB * 1024 * 1024);

        if (bundleBytes <= 0 || chunkBytes <= 0)
        {
            MessageBox.Show(this, "Calculated sizes must be greater than zero.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        return true;
    }

    private void SetJobRunningState(bool isRunning, string? status)
    {
        _isJobRunning = isRunning;
        if (isRunning)
        {
            _lastProgressPhase = null;
        }

        if (EncryptStartButton != null)
        {
            EncryptStartButton.IsEnabled = !isRunning;
        }

        if (DecryptStartButton != null)
        {
            DecryptStartButton.IsEnabled = !isRunning;
        }

        if (EncryptBrowseSourceButton != null)
        {
            EncryptBrowseSourceButton.IsEnabled = !isRunning;
        }

        if (EncryptBrowseDestinationButton != null)
        {
            EncryptBrowseDestinationButton.IsEnabled = !isRunning;
        }

        if (DecryptBrowseSourceButton != null)
        {
            DecryptBrowseSourceButton.IsEnabled = !isRunning;
        }

        if (DecryptBrowseDestinationButton != null)
        {
            DecryptBrowseDestinationButton.IsEnabled = !isRunning;
        }

        if (CancelJobButton != null)
        {
            CancelJobButton.IsEnabled = isRunning && _currentJobCts != null && !_currentJobCts.IsCancellationRequested;
        }
        JobProgressBar.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;

        if (isRunning)
        {
            JobProgressBar.Minimum = 0;
            JobProgressBar.Maximum = 100;
            JobProgressBar.Value = 0;
            JobProgressBar.IsIndeterminate = true; // Early phases: scanning/planning
        }
        else
        {
            JobProgressBar.IsIndeterminate = false;
            _currentJobCts?.Dispose();
            _currentJobCts = null;
        }

        if (!string.IsNullOrEmpty(status))
        {
            StatusTextBlock.Text = status;
        }
    }

    private void CancelJobButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isJobRunning)
        {
            return;
        }

        if (_currentJobCts == null || _currentJobCts.IsCancellationRequested)
        {
            return;
        }

        _currentJobCts.Cancel();
        StatusTextBlock.Text = "Cancel requested. Finishing current step...";
        _logger.Log("Job cancellation requested by user.");
        AppendMainLogLine("[Job] Cancel requested by user.");

        if (CancelJobButton != null)
        {
            CancelJobButton.IsEnabled = false;
        }
    }

    private void UpdateProgress(BackupProgress progress)
    {
        if (!_isJobRunning)
        {
            return;
        }

        Dispatcher.Invoke(() =>
        {
            JobProgressBar.IsIndeterminate = false;

            var percent = progress.PercentComplete;
            if (percent < 0)
            {
                percent = 0;
            }
            else if (percent > 100)
            {
                percent = 100;
            }

            JobProgressBar.Value = percent;

            string bundleInfo = string.Empty;
            if (progress.TotalBundles > 0)
            {
                bundleInfo = $" ({progress.CurrentBundleIndex} of {progress.TotalBundles} bundles)";
            }

            string itemInfo = string.IsNullOrEmpty(progress.CurrentItemName)
                ? string.Empty
                : $" - {progress.CurrentItemName}";

            StatusTextBlock.Text = $"{progress.Phase}: {percent:0.0}%{bundleInfo}{itemInfo}";

            if (!string.IsNullOrEmpty(progress.Phase) && progress.Phase != _lastProgressPhase)
            {
                _lastProgressPhase = progress.Phase;

                string phaseInfo;
                if (progress.TotalBundles > 0)
                {
                    phaseInfo = $"{progress.Phase} (bundle {progress.CurrentBundleIndex} of {progress.TotalBundles})";
                }
                else
                {
                    phaseInfo = progress.Phase;
                }

                AppendMainLogLine($"[Progress] {phaseInfo}");
            }
        });
    }

    private void AppendMainLogLine(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        void Append()
        {
            if (MainLogTextBox.Text.Length > 0)
            {
                MainLogTextBox.AppendText(Environment.NewLine);
            }

            MainLogTextBox.AppendText(message);
            TrimMainLogLines();
            MainLogTextBox.ScrollToEnd();
        }

        if (Dispatcher.CheckAccess())
        {
            Append();
        }
        else
        {
            Dispatcher.Invoke(Append);
        }
    }

    private void TrimMainLogLines()
    {
        var text = MainLogTextBox.Text;
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        if (lines.Length <= MainLogMaxLines)
        {
            return;
        }

        var startIndex = lines.Length - MainLogMaxLines;
        var trimmed = string.Join(Environment.NewLine, lines, startIndex, MainLogMaxLines);
        MainLogTextBox.Text = trimmed;
    }

    private void HelpButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new HelpWindow
        {
            Owner = this
        };

        window.ShowDialog();
    }

    private void AboutButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new AboutWindow
        {
            Owner = this
        };

        window.ShowDialog();
    }
}