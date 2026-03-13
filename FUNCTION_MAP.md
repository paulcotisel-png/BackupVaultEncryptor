# FUNCTION_MAP.md

## Current status
WPF project skeleton with initial configuration, logging, and local SQLite bootstrap wired into application startup.

## Areas
- UI: MainWindow with Main, Settings, Logs tabs
- Core/Infrastructure:
  - `AppConfig`
    - Properties: `AppDataDirectory`, `DatabaseFileName`, `LogFileName`
  - `AppConfigLoader`
    - `LoadConfiguration()`
      - Loads `appsettings.json` from the application directory
      - Binds values into `AppConfig`
      - Ensures simple defaults if values are missing
- Data:
  - `SqliteBootstrap`
    - `InitializeDatabase()`
      - Ensures the app data directory exists
      - Builds the SQLite database path from `AppConfig`
      - Opens/creates the SQLite database file
      - Creates a simple `Logs` table with `CREATE TABLE IF NOT EXISTS`
- Logging:
  - `AppLogger`
    - Constructor accepts `AppConfig` and resolves the log file path
    - `Log(string message)` writes timestamped lines to a text file
    - Maintains a small in-memory list of recent log entries
- Application startup:
  - `App` (in `App.xaml.cs`)
    - Overrides `OnStartup`
    - Loads configuration
    - Initializes `AppLogger`
    - Runs `SqliteBootstrap.InitializeDatabase()`
    - Writes simple startup log messages

## Notes
This file will be updated by Cline whenever new classes, services, or important functions are added.
Keep entries concise and practical.
