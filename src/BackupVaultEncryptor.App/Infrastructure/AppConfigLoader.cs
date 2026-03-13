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
            if (string.IsNullOrWhiteSpace(appDataDirectory))
            {
                appDataDirectory = "AppData";
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