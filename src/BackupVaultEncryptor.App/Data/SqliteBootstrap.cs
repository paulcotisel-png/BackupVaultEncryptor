using Microsoft.Data.Sqlite;
using System;
using System.IO;
using BackupVaultEncryptor.App.Infrastructure;

namespace BackupVaultEncryptor.App.Data
{
    /// <summary>
    /// Creates and initializes the local SQLite database used by the application.
    /// </summary>
    public class SqliteBootstrap
    {
        private readonly AppConfig _config;

        public SqliteBootstrap(AppConfig config)
        {
            _config = config;
        }

        public void InitializeDatabase()
        {
            // Work out the full path to the app data directory.
            var dataDirectory = _config.AppDataDirectory;
            if (string.IsNullOrWhiteSpace(dataDirectory))
            {
                dataDirectory = "AppData";
            }

            if (!Path.IsPathRooted(dataDirectory))
            {
                dataDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dataDirectory);
            }

            Directory.CreateDirectory(dataDirectory);

            var databaseFileName = string.IsNullOrWhiteSpace(_config.DatabaseFileName)
                ? "appdata.db"
                : _config.DatabaseFileName;

            var databasePath = Path.Combine(dataDirectory, databaseFileName);

            using (var connection = new SqliteConnection($"Data Source={databasePath}"))
            {
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText =
                @"
                    CREATE TABLE IF NOT EXISTS Logs (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Timestamp TEXT NOT NULL,
                        Message TEXT NOT NULL
                    );
                ";
                command.ExecuteNonQuery();
            }
        }
    }
}
