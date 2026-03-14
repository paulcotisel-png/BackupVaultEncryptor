using Microsoft.Extensions.Configuration;
using System;
using System.IO;

namespace BackupVaultEncryptor.App.Infrastructure
{
    /// <summary>
    /// Loads application configuration from appsettings.json into an AppConfig instance.
    /// </summary>
    public static class AppConfigLoader
    {
        public static AppConfig LoadConfiguration()
        {
            // Use the application base directory so the config file sits next to the executable.
            var basePath = AppDomain.CurrentDomain.BaseDirectory;

            var builder = new ConfigurationBuilder()
                .SetBasePath(basePath)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

            IConfigurationRoot configuration = builder.Build();

            // Read values directly from configuration with simple defaults.
            var appDataDirectory = configuration["AppDataDirectory"];

            // If no explicit AppDataDirectory is configured, default to a
            // per-user LocalAppData folder and, if possible, migrate from
            // the old app-folder AppData location.
            if (string.IsNullOrWhiteSpace(appDataDirectory))
            {
                var oldDefaultDirectory = Path.Combine(basePath, "AppData");

                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var newDefaultDirectory = Path.Combine(localAppData, "BackupVaultEncryptor");

                // Migration policy:
                // - If the old app-folder AppData exists and the new
                //   LocalAppData folder does not, attempt a one-time move.
                // - If the move succeeds, use the new LocalAppData
                //   directory.
                // - If the move fails, fall back to the old directory so
                //   existing data remains usable.
                // - If the new LocalAppData folder already exists, always
                //   use it and leave the old folder untouched.
                if (Directory.Exists(oldDefaultDirectory) && !Directory.Exists(newDefaultDirectory))
                {
                    try
                    {
                        Directory.Move(oldDefaultDirectory, newDefaultDirectory);
                        appDataDirectory = newDefaultDirectory;
                    }
                    catch
                    {
                        // If the move fails for any reason, fall back to
                        // the old location so the app can still function.
                        appDataDirectory = oldDefaultDirectory;
                    }
                }
                else
                {
                    // Either this is a fresh install with no old data, or
                    // the new directory already exists. In both cases we
                    // prefer the LocalAppData location.
                    appDataDirectory = newDefaultDirectory;
                }
            }

            var databaseFileName = configuration["DatabaseFileName"];
            if (string.IsNullOrWhiteSpace(databaseFileName))
            {
                databaseFileName = "appdata.db";
            }

            var logFileName = configuration["LogFileName"];
            if (string.IsNullOrWhiteSpace(logFileName))
            {
                logFileName = "app.log";
            }

            var config = new AppConfig
            {
                AppDataDirectory = appDataDirectory,
                DatabaseFileName = databaseFileName,
                LogFileName = logFileName
            };

            return config;
        }
    }
}