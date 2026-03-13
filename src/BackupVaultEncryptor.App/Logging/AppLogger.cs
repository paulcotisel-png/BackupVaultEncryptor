using System;
using System.Collections.Generic;
using System.IO;
using BackupVaultEncryptor.App.Infrastructure;

namespace BackupVaultEncryptor.App.Logging
{
    /// <summary>
    /// Very simple application logger that writes to a text file and keeps
    /// a small in-memory list of recent log entries.
    /// </summary>
    public class AppLogger
    {
        private readonly string _logFilePath;
        private readonly List<string> _recentEntries = new List<string>();

        /// <summary>
        /// A read-only view of the most recent log entries kept in memory.
        /// </summary>
        public IReadOnlyList<string> RecentEntries => _recentEntries.AsReadOnly();

        public AppLogger(AppConfig config)
        {
            var dataDirectory = config.AppDataDirectory;
            if (string.IsNullOrWhiteSpace(dataDirectory))
            {
                dataDirectory = "AppData";
            }

            if (!Path.IsPathRooted(dataDirectory))
            {
                dataDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dataDirectory);
            }

            Directory.CreateDirectory(dataDirectory);

            var logFileName = string.IsNullOrWhiteSpace(config.LogFileName)
                ? "app.log"
                : config.LogFileName;

            _logFilePath = Path.Combine(dataDirectory, logFileName);
        }

        /// <summary>
        /// Writes a message to the log file and stores it in the in-memory list.
        /// </summary>
        public void Log(string message)
        {
            if (message == null)
            {
                message = string.Empty;
            }

            var timestampedMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}";

            _recentEntries.Add(timestampedMessage);

            // Keep only a modest number of recent messages in memory
            if (_recentEntries.Count > 100)
            {
                _recentEntries.RemoveAt(0);
            }

            File.AppendAllText(_logFilePath, timestampedMessage + Environment.NewLine);
        }
    }
}