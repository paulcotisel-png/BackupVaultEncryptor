namespace BackupVaultEncryptor.App.Infrastructure
{
    /// <summary>
    /// Simple configuration model bound from appsettings.json.
    /// </summary>
    public class AppConfig
    {
        /// <summary>
        /// Directory where the application will store its local data
        /// (SQLite database, log files, etc.). Can be absolute or relative.
        /// </summary>
        public string AppDataDirectory { get; set; } = "AppData";

        /// <summary>
        /// File name of the SQLite database that lives inside AppDataDirectory.
        /// </summary>
        public string DatabaseFileName { get; set; } = "appdata.db";

        /// <summary>
        /// File name of the log file that lives inside AppDataDirectory.
        /// </summary>
        public string LogFileName { get; set; } = "app.log";
    }
}