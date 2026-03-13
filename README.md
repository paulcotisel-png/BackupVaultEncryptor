# BackupVaultEncryptor

## Overview
BackupVaultEncryptor is a Windows desktop application for encrypting and decrypting folders/files, primarily for backup encryption.

## Build and Run
1. Ensure you have .NET 8 SDK installed.
2. Restore packages and build the WPF application:
   ```
   cd src/BackupVaultEncryptor.App
   dotnet build
   ```
3. (Optional) Run the MSTest unit tests:
   ```
   cd ../BackupVaultEncryptor.Tests
   dotnet test
   ```
4. Run the application from the app project directory:
   ```
   cd ../BackupVaultEncryptor.App
   dotnet run
   ```

## Project Structure
- **UI**: Contains the MainWindow with Main, Settings, Logs tabs.
- **Core**: Contains core configuration management.
- **Infrastructure**: Contains application configuration and setup.
- **Security**: Contains password hashing, key derivation, DPAPI wrapper, and key wrapping helpers.
- **Services**: Contains authentication and vault/key management services.
- **Data**: Contains SQLite bootstrap for database initialization and thin repositories for users and root keys.
- **Logging**: Contains logging setup.

## Future Enhancements
- Implement encryption logic.
- Add more detailed logging.
- Expand UI features.