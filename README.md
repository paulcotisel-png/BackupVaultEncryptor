# BackupVaultEncryptor

## Overview
BackupVaultEncryptor is a Windows desktop application for encrypting and decrypting folders/files, primarily for backup encryption.

## Build and Run
1. Ensure you have .NET 8 SDK installed.
2. Navigate to the project directory:
   ```
   cd src/BackupVaultEncryptor.App
   ```
3. Build the project:
   ```
   dotnet build
   ```
4. Run the application:
   ```
   dotnet run
   ```

## Project Structure
- **UI**: Contains the MainWindow with Main, Settings, Logs tabs.
- **Core**: Contains core configuration management.
- **Infrastructure**: Contains application configuration and setup.
- **Security**: Placeholder for future security features.
- **Services**: Placeholder for future services.
- **Data**: Contains SQLite bootstrap for database initialization.
- **Logging**: Contains logging setup.

## Future Enhancements
- Implement encryption logic.
- Add more detailed logging.
- Expand UI features.