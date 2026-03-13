using BackupVaultEncryptor.App.Core.Models;
using BackupVaultEncryptor.App.Services;

namespace BackupVaultEncryptor.App.Core
{
    /// <summary>
    /// Holds the current user session and user settings for the running app.
    /// Kept very simple so it can be passed into windows that need it.
    /// </summary>
    public class AppState
    {
        public UserSession? CurrentSession { get; set; }
        public UserSettings CurrentSettings { get; set; } = new UserSettings();
    }
}
