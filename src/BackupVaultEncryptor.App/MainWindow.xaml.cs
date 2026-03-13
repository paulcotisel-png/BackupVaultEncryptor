using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using BackupVaultEncryptor.App.Core.BackupEngine;
using BackupVaultEncryptor.App.Core;
using BackupVaultEncryptor.App.Infrastructure;
using BackupVaultEncryptor.App.Logging;
using Microsoft.Win32;

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

        // Wire logs list to recent entries
        LogsListBox.ItemsSource = _logger.RecentEntries;

        UpdateModeUi();
    }

    private void BrowseSourceFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog();
        var result = dialog.ShowDialog();
        if (result == true && !string.IsNullOrEmpty(dialog.FolderName))
        {
            SourceTextBox.Text = dialog.FolderName;
        }
    }

    private void BrowseSourceFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog();

        if (IsEncryptMode())
        {
            dialog.Title = "Select file to encrypt";
        }
        else
        {
            dialog.Title = "Select manifest file";
            dialog.Filter = "Manifest files (*.json)|*.json|All files (*.*)|*.*";
        }

        var result = dialog.ShowDialog();
        if (result == true && !string.IsNullOrEmpty(dialog.FileName))
        {
            SourceTextBox.Text = dialog.FileName;
        }
    }

    private void BrowseDestination_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog();
        var result = dialog.ShowDialog();
        if (result == true && !string.IsNullOrEmpty(dialog.FolderName))
        {
            DestinationTextBox.Text = dialog.FolderName;
        }
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
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

        if (!IsEncryptMode())
        {
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
        else
        {
            // Encrypt mode: source can be folder or single file; destination is job output folder.
            bool isFolder = Directory.Exists(source);
            bool isFile = File.Exists(source);

            if (!isFolder && !isFile)
            {
                MessageBox.Show(this, "Source path does not exist.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
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

            await RunEncryptAsync(source, destination, isFolder, isFile);
        }
    }

    private async Task RunEncryptAsync(string sourcePath, string destinationRoot, bool isFolder, bool isFile)
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

        SetJobRunningState(true, "Scanning and planning...");

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

            await _encryptor.EncryptAsync(request, progress);

            StatusTextBlock.Text = "Encryption completed successfully.";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Encryption failed: {ex.Message}";
            _logger.Log($"Encryption failed: {ex}");
            MessageBox.Show(this, "Encryption failed. See logs tab for details.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetJobRunningState(false, null);
        }
    }

    private async Task RunDecryptAsync(string manifestPath, string destinationRoot)
    {
        SetJobRunningState(true, "Decrypting...");

        try
        {
            var request = new BackupJobDecryptRequest
            {
                ManifestPath = manifestPath,
                DestinationRoot = destinationRoot,
                UserSession = _appState.CurrentSession!
            };

            var progress = new Progress<BackupProgress>(UpdateProgress);

            await _decryptor.DecryptAsync(request, progress);

            StatusTextBlock.Text = "Decryption completed successfully.";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Decryption failed: {ex.Message}";
            _logger.Log($"Decryption failed: {ex}");
            MessageBox.Show(this, "Decryption failed. See logs tab for details.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

    private bool IsEncryptMode()
    {
        return ModeComboBox.SelectedIndex == 0;
    }

    private void SetJobRunningState(bool isRunning, string? status)
    {
        _isJobRunning = isRunning;

        StartButton.IsEnabled = !isRunning;
        BrowseSourceFolderButton.IsEnabled = !isRunning && IsEncryptMode();
        BrowseSourceFileButton.IsEnabled = !isRunning;
        BrowseDestinationButton.IsEnabled = !isRunning;
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
        }

        if (!string.IsNullOrEmpty(status))
        {
            StatusTextBlock.Text = status;
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
        });
    }

    private void ModeComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        UpdateModeUi();
    }

    private void UpdateModeUi()
    {
        if (BrowseSourceFolderButton == null || BrowseSourceFileButton == null)
        {
            return;
        }

        if (IsEncryptMode())
        {
            BrowseSourceFolderButton.IsEnabled = true;
            BrowseSourceFileButton.Content = "File...";
        }
        else
        {
            // Decrypt mode: source should be a manifest file, so folder browse is not useful.
            BrowseSourceFolderButton.IsEnabled = false;
            BrowseSourceFileButton.Content = "Manifest...";
        }
    }
}