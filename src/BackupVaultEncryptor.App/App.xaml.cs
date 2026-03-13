using System;
using System.Windows;
using BackupVaultEncryptor.App.Core;
using BackupVaultEncryptor.App.Core.BackupEngine;
using BackupVaultEncryptor.App.Data;
using BackupVaultEncryptor.App.Infrastructure;
using BackupVaultEncryptor.App.Logging;
using BackupVaultEncryptor.App.Security;
using BackupVaultEncryptor.App.Services;
using BackupVaultEncryptor.App.UI;

namespace BackupVaultEncryptor.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private AppConfig? _config;
    private AppLogger? _logger;
    private SqliteBootstrap? _sqliteBootstrap;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            // Load configuration from appsettings.json
            _config = AppConfigLoader.LoadConfiguration();

            // Set up logging based on configuration
            _logger = new AppLogger(_config);
            _logger.Log("Config loaded.");
            _logger.Log("Logger initialized.");
            _logger.Log("Application starting up.");

            // Initialize the SQLite database
            _sqliteBootstrap = new SqliteBootstrap(_config);
            _sqliteBootstrap.InitializeDatabase();

            _logger.Log("Database initialized.");

            // Compose core services
            var appState = new AppState();

            var userRepository = new UserRepository(_config);
            var vaultKeyRepository = new VaultKeyRepository(_config);
            var passwordHasher = new PasswordHasher();
            var passwordKeyDeriver = new PasswordKeyDeriver();
            var keyWrapService = new KeyWrapService();
            var localSecretProtector = new DpapiLocalSecretProtector();

            var authService = new AuthService(userRepository, passwordHasher, passwordKeyDeriver, keyWrapService, localSecretProtector, _logger);
            var vaultService = new VaultService(vaultKeyRepository, keyWrapService, _logger);

            var fileScanner = new FileScanner();
            var bundlePlanner = new BundlePlanner();
            var manifestStorage = new ManifestStorage();

            var encryptor = new BundleEncryptor(fileScanner, bundlePlanner, manifestStorage, vaultService, _logger);
            var decryptor = new BundleDecryptor(manifestStorage, vaultService, _logger);

            var settingsStore = new UserSettingsStore(_config);
            appState.CurrentSettings = settingsStore.LoadOrDefault();
            _logger.Log("Core services composed.");

            // Ensure app does not shut down automatically when auth dialogs close.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Decide whether to show first-user setup or login
            _logger.Log("User existence check starting.");
            bool hasUsers = userRepository.AnyUsers();
            _logger.Log($"User existence check completed. hasUsers={hasUsers}.");

            bool? authResult;
            if (!hasUsers)
            {
                _logger.Log("No users found. Showing first-user setup window.");
                var firstUserWindow = new FirstUserSetupWindow(authService, appState, _logger);
                authResult = firstUserWindow.ShowDialog();
            }
            else
            {
                _logger.Log("Existing users found. Showing login window.");
                var loginWindow = new LoginWindow(authService, appState);
                authResult = loginWindow.ShowDialog();
            }

            _logger.Log($"Auth dialog completed. Result={authResult}.");
            _logger.Log($"Session present after auth: {(appState.CurrentSession != null ? "yes" : "no")}.");

            if (authResult != true || appState.CurrentSession == null)
            {
                _logger.Log("Authentication failed or cancelled, or session missing. Shutting down before showing main window.");
                Shutdown();
                return;
            }

            _logger.Log("Authentication succeeded. Preparing main window.");
            ShutdownMode = ShutdownMode.OnMainWindowClose;

            // Show main window wired to services
            _logger.Log("Main window about to show.");
            var mainWindow = new MainWindow(appState, encryptor, decryptor, _logger, settingsStore);
            MainWindow = mainWindow;
            mainWindow.Show();
            _logger.Log("Main window shown.");
        }
        catch (Exception ex)
        {
            try
            {
                _logger?.Log($"FATAL startup error: {ex}");
            }
            catch
            {
                // Ignore logging failures during fatal error handling.
            }

            var message = $"{ex.GetType().Name}: {ex.Message}";
            MessageBox.Show(
                message,
                "Startup error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(1);
        }
    }
}

