using System;
using System.Collections.Generic;
using System.Globalization;
using BackupVaultEncryptor.App.Core.Models;
using BackupVaultEncryptor.App.Infrastructure;
using Microsoft.Data.Sqlite;

namespace BackupVaultEncryptor.App.Data
{
    /// <summary>
    /// Thin repository for working with UserAccount rows in SQLite.
    /// Kept simple and explicit for readability.
    /// </summary>
    public class UserRepository
    {
        private readonly AppConfig _config;

        public UserRepository(AppConfig config)
        {
            _config = config;
        }

        private string GetDatabasePath()
        {
            var dataDirectory = _config.AppDataDirectory;
            if (string.IsNullOrWhiteSpace(dataDirectory))
            {
                dataDirectory = "AppData";
            }

            if (!System.IO.Path.IsPathRooted(dataDirectory))
            {
                dataDirectory = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dataDirectory);
            }

            var databaseFileName = string.IsNullOrWhiteSpace(_config.DatabaseFileName)
                ? "appdata.db"
                : _config.DatabaseFileName;

            return System.IO.Path.Combine(dataDirectory, databaseFileName);
        }

        private SqliteConnection CreateConnection()
        {
            var databasePath = GetDatabasePath();
            return new SqliteConnection($"Data Source={databasePath}");
        }

        public UserAccount? GetByUsername(string username)
        {
            using var connection = CreateConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText =
                @"SELECT Id, Username, RecoveryEmail, PasswordHash,
                         VaultMasterKeyWrappedCiphertext, VaultMasterKeyWrappedNonce, VaultMasterKeyWrappedTag,
                         VaultMasterKeyProtectedByDpapi, KeyWrapSalt,
                         PasswordResetTokenHash, PasswordResetTokenExpiresUtc,
                         CreatedUtc, UpdatedUtc
                  FROM Users
                  WHERE Username = @username";
            command.Parameters.AddWithValue("@username", username);

            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            return MapUser(reader);
        }

        public UserAccount? GetById(int id)
        {
            using var connection = CreateConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText =
                @"SELECT Id, Username, RecoveryEmail, PasswordHash,
                         VaultMasterKeyWrappedCiphertext, VaultMasterKeyWrappedNonce, VaultMasterKeyWrappedTag,
                         VaultMasterKeyProtectedByDpapi, KeyWrapSalt,
                         PasswordResetTokenHash, PasswordResetTokenExpiresUtc,
                         CreatedUtc, UpdatedUtc
                  FROM Users
                  WHERE Id = @id";
            command.Parameters.AddWithValue("@id", id);

            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            return MapUser(reader);
        }

        public int CreateUser(UserAccount user)
        {
            if (user is null) throw new ArgumentNullException(nameof(user));

            using var connection = CreateConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText =
                @"INSERT INTO Users (
                        Username, RecoveryEmail, PasswordHash,
                        VaultMasterKeyWrappedCiphertext, VaultMasterKeyWrappedNonce, VaultMasterKeyWrappedTag,
                        VaultMasterKeyProtectedByDpapi, KeyWrapSalt,
                        PasswordResetTokenHash, PasswordResetTokenExpiresUtc,
                        CreatedUtc, UpdatedUtc
                  ) VALUES (
                        @Username, @RecoveryEmail, @PasswordHash,
                        @VaultMasterKeyWrappedCiphertext, @VaultMasterKeyWrappedNonce, @VaultMasterKeyWrappedTag,
                        @VaultMasterKeyProtectedByDpapi, @KeyWrapSalt,
                        @PasswordResetTokenHash, @PasswordResetTokenExpiresUtc,
                        @CreatedUtc, @UpdatedUtc
                  );
                  SELECT last_insert_rowid();";

            AddCommonParameters(command, user);

            var result = command.ExecuteScalar();
            if (result is long id)
            {
                user.Id = (int)id;
                return user.Id;
            }

            throw new InvalidOperationException("Failed to retrieve new user id.");
        }

        public void UpdateUser(UserAccount user)
        {
            if (user is null) throw new ArgumentNullException(nameof(user));

            using var connection = CreateConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText =
                @"UPDATE Users SET
                        Username = @Username,
                        RecoveryEmail = @RecoveryEmail,
                        PasswordHash = @PasswordHash,
                        VaultMasterKeyWrappedCiphertext = @VaultMasterKeyWrappedCiphertext,
                        VaultMasterKeyWrappedNonce = @VaultMasterKeyWrappedNonce,
                        VaultMasterKeyWrappedTag = @VaultMasterKeyWrappedTag,
                        VaultMasterKeyProtectedByDpapi = @VaultMasterKeyProtectedByDpapi,
                        KeyWrapSalt = @KeyWrapSalt,
                        PasswordResetTokenHash = @PasswordResetTokenHash,
                        PasswordResetTokenExpiresUtc = @PasswordResetTokenExpiresUtc,
                        CreatedUtc = @CreatedUtc,
                        UpdatedUtc = @UpdatedUtc
                  WHERE Id = @Id";

            AddCommonParameters(command, user);
            command.Parameters.AddWithValue("@Id", user.Id);

            command.ExecuteNonQuery();
        }

        private static void AddCommonParameters(SqliteCommand command, UserAccount user)
        {
            command.Parameters.AddWithValue("@Username", user.Username);
            command.Parameters.AddWithValue("@RecoveryEmail", (object?)user.RecoveryEmail ?? DBNull.Value);
            command.Parameters.AddWithValue("@PasswordHash", user.PasswordHash);
            command.Parameters.AddWithValue("@VaultMasterKeyWrappedCiphertext", user.VaultMasterKeyWrappedCiphertext);
            command.Parameters.AddWithValue("@VaultMasterKeyWrappedNonce", user.VaultMasterKeyWrappedNonce);
            command.Parameters.AddWithValue("@VaultMasterKeyWrappedTag", user.VaultMasterKeyWrappedTag);
            command.Parameters.AddWithValue("@VaultMasterKeyProtectedByDpapi", user.VaultMasterKeyProtectedByDpapi);
            command.Parameters.AddWithValue("@KeyWrapSalt", user.KeyWrapSalt);
            command.Parameters.AddWithValue("@PasswordResetTokenHash", (object?)user.PasswordResetTokenHash ?? DBNull.Value);
            command.Parameters.AddWithValue("@PasswordResetTokenExpiresUtc", user.PasswordResetTokenExpiresUtc?.ToString("o") ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@CreatedUtc", user.CreatedUtc.ToString("o"));
            command.Parameters.AddWithValue("@UpdatedUtc", user.UpdatedUtc.ToString("o"));
        }

        private static UserAccount MapUser(SqliteDataReader reader)
        {
            var user = new UserAccount
            {
                Id = reader.GetInt32(0),
                Username = reader.GetString(1),
                RecoveryEmail = reader.IsDBNull(2) ? null : reader.GetString(2),
                PasswordHash = reader.GetString(3),
                VaultMasterKeyWrappedCiphertext = (byte[])reader[4],
                VaultMasterKeyWrappedNonce = (byte[])reader[5],
                VaultMasterKeyWrappedTag = (byte[])reader[6],
                VaultMasterKeyProtectedByDpapi = (byte[])reader[7],
                KeyWrapSalt = (byte[])reader[8],
                PasswordResetTokenHash = reader.IsDBNull(9) ? null : reader.GetString(9),
                PasswordResetTokenExpiresUtc = reader.IsDBNull(10)
                    ? (DateTimeOffset?)null
                    : DateTimeOffset.Parse(reader.GetString(10), null, DateTimeStyles.RoundtripKind),
                CreatedUtc = DateTimeOffset.Parse(reader.GetString(11), null, DateTimeStyles.RoundtripKind),
                UpdatedUtc = DateTimeOffset.Parse(reader.GetString(12), null, DateTimeStyles.RoundtripKind)
            };

            return user;
        }
    }
}
