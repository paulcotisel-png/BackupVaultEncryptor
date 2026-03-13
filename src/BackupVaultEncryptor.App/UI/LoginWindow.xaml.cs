using System;
using System.Windows;
using BackupVaultEncryptor.App.Core;
using BackupVaultEncryptor.App.Services;

namespace BackupVaultEncryptor.App.UI
{
    public partial class LoginWindow : Window
    {
        private readonly AuthService _authService;
        private readonly AppState _appState;

        public LoginWindow(AuthService authService, AppState appState)
        {
            _authService = authService;
            _appState = appState;
            InitializeComponent();
        }

        private void Login_Click(object sender, RoutedEventArgs e)
        {
            ErrorTextBlock.Text = string.Empty;

            var username = UsernameTextBox.Text?.Trim() ?? string.Empty;
            var password = PasswordBox.Password ?? string.Empty;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            {
                ErrorTextBlock.Text = "Username and password are required.";
                return;
            }

            try
            {
                var session = _authService.Login(username, password);
                if (session == null)
                {
                    ErrorTextBlock.Text = "Invalid username or password.";
                    return;
                }

                _appState.CurrentSession = session;
                DialogResult = true;
                Close();
            }
            catch (AuthService.LoginInternalException)
            {
                // Credentials were valid, but something failed after verification.
                ErrorTextBlock.Text = "Login could not be completed. Please check the logs and try again.";
            }
            catch (Exception)
            {
                // For unexpected errors that are not part of the login flow contract,
                // show a simple generic message.
                ErrorTextBlock.Text = "An unexpected error occurred during login. Please try again.";
            }
        }
    }
}
