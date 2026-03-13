using System.Windows;
using BackupVaultEncryptor.App.Data;
using BackupVaultEncryptor.App.Infrastructure;
using BackupVaultEncryptor.App.Logging;

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

        // Load configuration from appsettings.json
        _config = AppConfigLoader.LoadConfiguration();

        // Set up logging based on configuration
        _logger = new AppLogger(_config);
        _logger.Log("Application starting up.");

        // Initialize the SQLite database
        _sqliteBootstrap = new SqliteBootstrap(_config);
        _sqliteBootstrap.InitializeDatabase();

        _logger.Log("Database initialized.");
    }
}

