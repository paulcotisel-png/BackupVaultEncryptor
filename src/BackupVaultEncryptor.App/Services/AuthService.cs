using System;
using System.Security.Cryptography;
using System.Text;
using BackupVaultEncryptor.App.Core.Models;
using BackupVaultEncryptor.App.Data;
using BackupVaultEncryptor.App.Logging;
using BackupVaultEncryptor.App.Security;

namespace BackupVaultEncryptor.App.Services
{
    /// <summary>
    /// Handles local user authentication, password hashing, and vault master key management.
    /// </summary>
    public class AuthService
    {
        private readonly UserRepository _userRepository;
        private readonly PasswordHasher _passwordHasher;
        private readonly PasswordKeyDeriver _passwordKeyDeriver;
        private readonly KeyWrapService _keyWrapService;
        private readonly ILocalSecretProtector _localSecretProtector;
        private readonly AppLogger _logger;

        private const string VaultMasterKeyWrapPurpose = "VaultMasterKeyWrap";

        public AuthService(
            UserRepository userRepository,
            PasswordHasher passwordHasher,
            PasswordKeyDeriver passwordKeyDeriver,
            KeyWrapService keyWrapService,
            ILocalSecretProtector localSecretProtector,
            AppLogger logger)
        {
            _userRepository = userRepository;
            _passwordHasher = passwordHasher;
            _passwordKeyDeriver = passwordKeyDeriver;
            _keyWrapService = keyWrapService;
            _localSecretProtector = localSecretProtector;
            _logger = logger;
        }

        public UserAccount RegisterUser(string username, string password, string? recoveryEmail)
        {
            if (string.IsNullOrWhiteSpace(username)) throw new ArgumentException("Username is required.", nameof(username));
            if (string.IsNullOrEmpty(password)) throw new ArgumentException("Password is required.", nameof(password));

            var existing = _userRepository.GetByUsername(username);
            if (existing != null)
            {
                throw new InvalidOperationException("User already exists.");
            }

            var now = DateTimeOffset.UtcNow;

            // 1. Hash password for login verification.
            var passwordHash = _passwordHasher.HashPassword(password);

            // 2. Generate random VaultMasterKey (256-bit).
            var vaultMasterKey = RandomNumberGenerator.GetBytes(32);

            // 3. Generate KeyWrapSalt and derive PasswordDerivedKey for wrapping VMK.
            var keyWrapSalt = RandomNumberGenerator.GetBytes(16);
            var passwordDerivedKey = _passwordKeyDeriver.DeriveKey(password, keyWrapSalt, 32, VaultMasterKeyWrapPurpose);

            // 4. Wrap VMK with AES-GCM using derived key.
            var wrapped = _keyWrapService.WrapWithKey(vaultMasterKey, passwordDerivedKey);

            // 5. Protect VMK with DPAPI for password reset scenarios.
            var protectedByDpapi = _localSecretProtector.Protect(vaultMasterKey);

            var user = new UserAccount
            {
                Username = username,
                RecoveryEmail = recoveryEmail,
                PasswordHash = passwordHash,
                VaultMasterKeyWrappedCiphertext = wrapped.Ciphertext,
                VaultMasterKeyWrappedNonce = wrapped.Nonce,
                VaultMasterKeyWrappedTag = wrapped.Tag,
                VaultMasterKeyProtectedByDpapi = protectedByDpapi,
                KeyWrapSalt = keyWrapSalt,
                PasswordResetTokenHash = null,
                PasswordResetTokenExpiresUtc = null,
                CreatedUtc = now,
                UpdatedUtc = now
            };

            _userRepository.CreateUser(user);
            _logger.Log($"User '{username}' registered.");

            // Clear sensitive material from memory where practical.
            CryptographicOperations.ZeroMemory(vaultMasterKey);
            CryptographicOperations.ZeroMemory(passwordDerivedKey);

            return user;
        }

        public UserSession? Login(string username, string password)
        {
            _logger.Log($"Login attempt started for username '{username}'.");

            var user = _userRepository.GetByUsername(username);
            if (user == null)
            {
                _logger.Log($"Login user lookup: user '{username}' not found.");
                return null;
            }

            if (!_passwordHasher.VerifyPassword(password, user.PasswordHash))
            {
                _logger.Log($"Login password verification FAILED for user Id {user.Id}.");
                return null;
            }

            _logger.Log($"Login password verification passed for user Id {user.Id}.");

            byte[]? passwordDerivedKey = null;

            try
            {
                _logger.Log($"Login VMK unwrap starting for user Id {user.Id}.");

                // Derive wrapping key and unwrap the VaultMasterKey.
                passwordDerivedKey = _passwordKeyDeriver.DeriveKey(password, user.KeyWrapSalt, 32, VaultMasterKeyWrapPurpose);

                var wrapped = new Security.WrappedKeyData(
                    user.VaultMasterKeyWrappedCiphertext,
                    user.VaultMasterKeyWrappedNonce,
                    user.VaultMasterKeyWrappedTag);

                var vaultMasterKey = _keyWrapService.UnwrapWithKey(wrapped, passwordDerivedKey);

                _logger.Log($"Login VMK unwrap succeeded for user Id {user.Id}.");

                var session = new UserSession(user.Id, user.Username, vaultMasterKey);

                _logger.Log($"Login session created for user Id {user.Id}.");

                return session;
            }
            catch (Exception ex)
            {
                _logger.Log($"Login internal failure after password verification for user Id {user.Id}: {ex.GetType().Name}: {ex.Message}");
                throw new LoginInternalException("Login could not be completed due to an internal error.", ex);
            }
            finally
            {
                if (passwordDerivedKey != null)
                {
                    CryptographicOperations.ZeroMemory(passwordDerivedKey);
                }
            }
        }

        /// <summary>
        /// Represents an internal failure that occurred after credentials were verified
        /// (for example, while unwrapping the VaultMasterKey or creating the session).
        /// This allows the UI layer to distinguish between bad credentials and
        /// post-verification internal errors without exposing low-level details.
        /// </summary>
        public class LoginInternalException : Exception
        {
            public LoginInternalException(string message, Exception innerException)
                : base(message, innerException)
            {
            }
        }

        public void ChangePassword(int userId, string oldPassword, string newPassword)
        {
            if (string.IsNullOrEmpty(newPassword)) throw new ArgumentException("New password is required.", nameof(newPassword));

            var user = _userRepository.GetById(userId) ?? throw new InvalidOperationException("User not found.");

            if (!_passwordHasher.VerifyPassword(oldPassword, user.PasswordHash))
            {
                throw new InvalidOperationException("Old password is incorrect.");
            }

            // Unwrap existing VMK using old password.
            var oldDerivedKey = _passwordKeyDeriver.DeriveKey(oldPassword, user.KeyWrapSalt, 32, VaultMasterKeyWrapPurpose);
            var wrapped = new Security.WrappedKeyData(
                user.VaultMasterKeyWrappedCiphertext,
                user.VaultMasterKeyWrappedNonce,
                user.VaultMasterKeyWrappedTag);

            var vaultMasterKey = _keyWrapService.UnwrapWithKey(wrapped, oldDerivedKey);
            CryptographicOperations.ZeroMemory(oldDerivedKey);

            // Re-hash password and re-wrap VMK with new derived key.
            var now = DateTimeOffset.UtcNow;
            var newPasswordHash = _passwordHasher.HashPassword(newPassword);

            var newKeyWrapSalt = RandomNumberGenerator.GetBytes(16);
            var newDerivedKey = _passwordKeyDeriver.DeriveKey(newPassword, newKeyWrapSalt, 32, VaultMasterKeyWrapPurpose);
            var newWrapped = _keyWrapService.WrapWithKey(vaultMasterKey, newDerivedKey);

            user.PasswordHash = newPasswordHash;
            user.KeyWrapSalt = newKeyWrapSalt;
            user.VaultMasterKeyWrappedCiphertext = newWrapped.Ciphertext;
            user.VaultMasterKeyWrappedNonce = newWrapped.Nonce;
            user.VaultMasterKeyWrappedTag = newWrapped.Tag;
            // DPAPI-protected copy does not need to change.

            user.UpdatedUtc = now;

            _userRepository.UpdateUser(user);

            CryptographicOperations.ZeroMemory(vaultMasterKey);
            CryptographicOperations.ZeroMemory(newDerivedKey);

            _logger.Log($"User '{user.Username}' changed password.");
        }

        public string CreatePasswordResetToken(string username, TimeSpan validity)
        {
            var user = _userRepository.GetByUsername(username) ?? throw new InvalidOperationException("User not found.");

            // Generate a random token and store only its SHA-256 hash.
            var tokenBytes = RandomNumberGenerator.GetBytes(32);
            var token = Convert.ToBase64String(tokenBytes);

            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(token));
            var hashBase64 = Convert.ToBase64String(hashBytes);

            user.PasswordResetTokenHash = hashBase64;
            user.PasswordResetTokenExpiresUtc = DateTimeOffset.UtcNow.Add(validity);
            user.UpdatedUtc = DateTimeOffset.UtcNow;

            _userRepository.UpdateUser(user);

            // Return the raw token so the caller can send it via email or use it directly in tests.
            return token;
        }

        public void ResetPasswordWithToken(string username, string token, string newPassword)
        {
            if (string.IsNullOrEmpty(token)) throw new ArgumentException("Token is required.", nameof(token));
            if (string.IsNullOrEmpty(newPassword)) throw new ArgumentException("New password is required.", nameof(newPassword));

            var user = _userRepository.GetByUsername(username) ?? throw new InvalidOperationException("User not found.");

            if (string.IsNullOrEmpty(user.PasswordResetTokenHash) || user.PasswordResetTokenExpiresUtc == null)
            {
                throw new InvalidOperationException("No active password reset token.");
            }

            if (user.PasswordResetTokenExpiresUtc <= DateTimeOffset.UtcNow)
            {
                throw new InvalidOperationException("Password reset token has expired.");
            }

            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(token));
            var hashBase64 = Convert.ToBase64String(hashBytes);

            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(hashBase64), Encoding.UTF8.GetBytes(user.PasswordResetTokenHash)))
            {
                throw new InvalidOperationException("Invalid password reset token.");
            }

            // Recover VaultMasterKey from DPAPI-protected copy.
            var vaultMasterKey = _localSecretProtector.Unprotect(user.VaultMasterKeyProtectedByDpapi);

            var now = DateTimeOffset.UtcNow;

            // Hash new password and re-wrap same VMK with a new derived key.
            var newPasswordHash = _passwordHasher.HashPassword(newPassword);
            var newKeyWrapSalt = RandomNumberGenerator.GetBytes(16);
            var newDerivedKey = _passwordKeyDeriver.DeriveKey(newPassword, newKeyWrapSalt, 32, VaultMasterKeyWrapPurpose);
            var newWrapped = _keyWrapService.WrapWithKey(vaultMasterKey, newDerivedKey);

            user.PasswordHash = newPasswordHash;
            user.KeyWrapSalt = newKeyWrapSalt;
            user.VaultMasterKeyWrappedCiphertext = newWrapped.Ciphertext;
            user.VaultMasterKeyWrappedNonce = newWrapped.Nonce;
            user.VaultMasterKeyWrappedTag = newWrapped.Tag;
            // DPAPI-protected copy is unchanged.

            // Clear the token so it cannot be reused.
            user.PasswordResetTokenHash = null;
            user.PasswordResetTokenExpiresUtc = null;
            user.UpdatedUtc = now;

            _userRepository.UpdateUser(user);

            CryptographicOperations.ZeroMemory(vaultMasterKey);
            CryptographicOperations.ZeroMemory(newDerivedKey);

            _logger.Log($"User '{user.Username}' reset password using token.");
        }
    }
}
