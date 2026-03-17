using System;
using System.IO;
using System.Text.Json;
using BackupVaultEncryptor.App.Core.Models;

namespace BackupVaultEncryptor.App.Infrastructure
{
    /// <summary>
    /// Loads and saves simple user settings in a JSON file under AppData.
    /// This keeps runtime-tunable values separate from shipped appsettings.json.
    /// </summary>
    public class UserSettingsStore
    {
        private readonly AppConfig _config;
        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        public UserSettingsStore(AppConfig config)
        {
            _config = config;
        }

        private string GetSettingsPath()
        {
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

            return Path.Combine(dataDirectory, "user-settings.json");
        }

        public UserSettings LoadOrDefault()
        {
            var path = GetSettingsPath();
            if (!File.Exists(path))
            {
                return new UserSettings();
            }

            try
            {
                var json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<UserSettings>(json, SerializerOptions);
                return settings ?? new UserSettings();
            }
            catch
            {
                // If anything goes wrong, fall back to safe defaults.
                return new UserSettings();
            }
        }

        public void Save(UserSettings settings)
        {
            var path = GetSettingsPath();
            var json = JsonSerializer.Serialize(settings, SerializerOptions);
            File.WriteAllText(path, json);
        }

        public string GetDefaultScratchPath()
        {
            var dataDirectory = _config.AppDataDirectory;
            if (string.IsNullOrWhiteSpace(dataDirectory))
            {
                dataDirectory = "AppData";
            }

            if (!Path.IsPathRooted(dataDirectory))
            {
                dataDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dataDirectory);
            }

            return Path.Combine(dataDirectory, "Scratch");
        }
    }
}
