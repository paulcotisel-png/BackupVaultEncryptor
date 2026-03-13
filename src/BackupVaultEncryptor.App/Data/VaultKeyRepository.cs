using System;
using System.Collections.Generic;
using System.Globalization;
using BackupVaultEncryptor.App.Core.Models;
using BackupVaultEncryptor.App.Infrastructure;
using Microsoft.Data.Sqlite;

namespace BackupVaultEncryptor.App.Data
{
    /// <summary>
    /// Thin repository for working with RootKeys in SQLite.
    /// </summary>
    public class VaultKeyRepository
    {
        private readonly AppConfig _config;

        public VaultKeyRepository(AppConfig config)
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

        public RootKeyRecord? GetRootKey(int userId, string rootPath)
        {
            using var connection = CreateConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText =
                @"SELECT Id, UserId, RootPath,
                         RootKeyEncryptedCiphertext, RootKeyEncryptedNonce, RootKeyEncryptedTag,
                         CreatedUtc
                  FROM RootKeys
                  WHERE UserId = @userId AND RootPath = @rootPath";
            command.Parameters.AddWithValue("@userId", userId);
            command.Parameters.AddWithValue("@rootPath", rootPath);

            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            return MapRootKey(reader);
        }

        public IReadOnlyList<RootKeyRecord> GetRootKeysForUser(int userId)
        {
            var results = new List<RootKeyRecord>();

            using var connection = CreateConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText =
                @"SELECT Id, UserId, RootPath,
                         RootKeyEncryptedCiphertext, RootKeyEncryptedNonce, RootKeyEncryptedTag,
                         CreatedUtc
                  FROM RootKeys
                  WHERE UserId = @userId";
            command.Parameters.AddWithValue("@userId", userId);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                results.Add(MapRootKey(reader));
            }

            return results;
        }

        public int AddRootKey(RootKeyRecord rootKey)
        {
            if (rootKey is null) throw new ArgumentNullException(nameof(rootKey));

            using var connection = CreateConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText =
                @"INSERT INTO RootKeys (
                        UserId, RootPath,
                        RootKeyEncryptedCiphertext, RootKeyEncryptedNonce, RootKeyEncryptedTag,
                        CreatedUtc
                  ) VALUES (
                        @UserId, @RootPath,
                        @RootKeyEncryptedCiphertext, @RootKeyEncryptedNonce, @RootKeyEncryptedTag,
                        @CreatedUtc
                  );
                  SELECT last_insert_rowid();";

            command.Parameters.AddWithValue("@UserId", rootKey.UserId);
            command.Parameters.AddWithValue("@RootPath", rootKey.RootPath);
            command.Parameters.AddWithValue("@RootKeyEncryptedCiphertext", rootKey.RootKeyEncryptedCiphertext);
            command.Parameters.AddWithValue("@RootKeyEncryptedNonce", rootKey.RootKeyEncryptedNonce);
            command.Parameters.AddWithValue("@RootKeyEncryptedTag", rootKey.RootKeyEncryptedTag);
            command.Parameters.AddWithValue("@CreatedUtc", rootKey.CreatedUtc.ToString("o"));

            var result = command.ExecuteScalar();
            if (result is long id)
            {
                rootKey.Id = (int)id;
                return rootKey.Id;
            }

            throw new InvalidOperationException("Failed to retrieve new root key id.");
        }

        private static RootKeyRecord MapRootKey(SqliteDataReader reader)
        {
            var record = new RootKeyRecord
            {
                Id = reader.GetInt32(0),
                UserId = reader.GetInt32(1),
                RootPath = reader.GetString(2),
                RootKeyEncryptedCiphertext = (byte[])reader[3],
                RootKeyEncryptedNonce = (byte[])reader[4],
                RootKeyEncryptedTag = (byte[])reader[5],
                CreatedUtc = DateTimeOffset.Parse(reader.GetString(6), null, DateTimeStyles.RoundtripKind)
            };

            return record;
        }
    }
}
