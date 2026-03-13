using System;
using System.Windows;
using BackupVaultEncryptor.App.Core;
using BackupVaultEncryptor.App.Logging;
using BackupVaultEncryptor.App.Services;

namespace BackupVaultEncryptor.App.UI
{
    public partial class FirstUserSetupWindow : Window
    {
        private readonly AuthService _authService;
        private readonly AppState _appState;
        private readonly AppLogger _logger;
        private bool _isCreating;

        public FirstUserSetupWindow(AuthService authService, AppState appState, AppLogger logger)
        {
            _authService = authService;
            _appState = appState;
            _logger = logger;
            InitializeComponent();
        }

        private void Create_Click(object sender, RoutedEventArgs e)
        {
            if (_isCreating)
            {
                return;
            }

            ErrorTextBlock.Text = string.Empty;

            var username = UsernameTextBox.Text?.Trim() ?? string.Empty;
            var password = PasswordBox.Password ?? string.Empty;
            var confirm = ConfirmPasswordBox.Password ?? string.Empty;
            var email = RecoveryEmailTextBox.Text?.Trim();

            if (string.IsNullOrWhiteSpace(username))
            {
                ErrorTextBlock.Text = "Username is required.";
                return;
            }

            if (string.IsNullOrEmpty(password))
            {
                ErrorTextBlock.Text = "Password is required.";
                return;
            }

            if (!string.Equals(password, confirm, StringComparison.Ordinal))
            {
                ErrorTextBlock.Text = "Passwords do not match.";
                return;
            }

            var registrationSucceeded = false;

            try
            {
                _isCreating = true;
                CreateButton.IsEnabled = false;

                _logger.Log($"First user setup: create clicked for username '{username}'. Starting registration.");

                _authService.RegisterUser(username, password, email);
                _logger.Log($"First user setup: registration succeeded for username '{username}'.");
                registrationSucceeded = true;

                _logger.Log($"First user setup: attempting automatic login for username '{username}'.");
                var session = _authService.Login(username, password);

                if (session == null)
                {
                    _logger.Log($"First user setup: automatic login returned null for username '{username}'.");
                    MessageBox.Show(
                        "Account was created, but automatic login failed. Please restart the app and log in with your new account.",
                        "Login issue",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    _logger.Log("First user setup: closing window after auto-login failure.");
                    DialogResult = false;
                    Close();
                    return;
                }

                _logger.Log($"First user setup: automatic login succeeded for username '{username}'.");

                _appState.CurrentSession = session;
                _logger.Log("First user setup: setting DialogResult=true and closing window after successful registration and login.");
                DialogResult = true;
                Close();
            }
            catch (InvalidOperationException ex) when (string.Equals(ex.Message, "User already exists.", StringComparison.Ordinal))
            {
                _logger.Log($"First user setup: duplicate username attempted: '{username}'. {ex.GetType().Name}: {ex.Message}");
                ErrorTextBlock.Text = "This username already exists. Please log in instead or choose another username.";
                _isCreating = false;
                CreateButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                if (registrationSucceeded)
                {
                    _logger.Log($"First user setup: automatic login threw an exception for username '{username}'. {ex}");
                    MessageBox.Show(
                        "Account was created, but automatic login failed. Please restart the app and log in with your new account.",
                        "Login issue",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    _logger.Log("First user setup: closing window after auto-login failure due to exception.");
                    DialogResult = false;
                    Close();
                }
                else
                {
                    _logger.Log($"First user setup: unexpected error before registration completed for username '{username}'. {ex}");
                    ErrorTextBlock.Text = "Something went wrong while creating your account. Please try again.";
                    _isCreating = false;
                    CreateButton.IsEnabled = true;
                }
            }
        }
    }
}
