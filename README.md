# BackupVaultEncryptor

## Overview
BackupVaultEncryptor is a Windows desktop application for encrypting and decrypting folders/files, primarily for backup encryption.

## Build and Run
1. Ensure you have the **.NET 8 SDK** installed.
2. Restore packages and build the WPF application from the repository root:
   ```powershell
   dotnet build .\BackupVaultEncryptor.sln
   ```
3. (Optional) Run the MSTest unit tests:
   ```powershell
   dotnet test .\src\BackupVaultEncryptor.Tests\BackupVaultEncryptor.Tests.csproj
   ```
4. Run the application during development:
   ```powershell
   dotnet run --project .\src\BackupVaultEncryptor.App\BackupVaultEncryptor.App.csproj
   ```

## Project Structure
- **UI**: Contains the MainWindow with Main, Settings, Logs tabs.
- **Core**: Contains core configuration management.
- **Infrastructure**: Contains application configuration and setup.
- **Security**: Contains password hashing, key derivation, DPAPI wrapper, and key wrapping helpers.
- **Services**: Contains authentication and vault/key management services.
- **Data**: Contains SQLite bootstrap for database initialization and thin repositories for users and root keys.
- **Logging**: Contains logging setup.

## Publishing / Release (Windows v1)

### Recommended publish mode

The recommended v1 publish mode is:

- **Target OS/CPU**: Windows 64-bit (`win-x64`)
- **Mode**: framework-dependent
- **Layout**: normal multi-file publish (no single-file, no trimming)

This keeps the output folder small and beginner-friendly while relying on the official .NET 8 Desktop Runtime installed on the target machine.

### Prerequisites on the operator machine

Before running the published app on a Windows PC:

1. Install **.NET 8 Desktop Runtime (x64)** from Microsoft.
2. Ensure the user has normal Windows access to any SMB/network folders that will be used for encryption/decryption (the app does not manage SMB authentication itself).

### Primary publish command (framework-dependent, win-x64)

Run this from the **repository root** in PowerShell:

```powershell
dotnet publish .\src\BackupVaultEncryptor.App\BackupVaultEncryptor.App.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=false -p:PublishTrimmed=false
```

This produces a publish output folder under:

```text
src\BackupVaultEncryptor.App\bin\Release\net8.0-windows\win-x64\publish\
```

> Always keep the **entire `publish` folder** together. Do **not** copy only the `.exe`.

### Optional publish via MSBuild profile

There is also a simple publish profile for the same framework-dependent, win-x64, multi-file mode:

```powershell
dotnet publish .\src\BackupVaultEncryptor.App\BackupVaultEncryptor.App.csproj -c Release -p:PublishProfile=Win-x64-FD-v1
```

This uses the profile at:

```text
src\BackupVaultEncryptor.App\Properties\PublishProfiles\Win-x64-FD-v1.pubxml
```

The publish output folder is the same `publish` path as above.

### Optional alternative: self-contained publish (larger folder)

If you do **not** want to install the .NET 8 Desktop Runtime on the operator machine, you can publish a **self-contained** version instead. This produces a much larger folder but does not require a separate runtime installation.

Run from the repository root in PowerShell:

```powershell
dotnet publish .\src\BackupVaultEncryptor.App\BackupVaultEncryptor.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false
```

The output folder location is similar, but the contents will be larger because the .NET runtime is included.

### What the publish folder contains

In the `publish` folder you should see at least:

- `BackupVaultEncryptor.App.exe` (exact name may vary slightly depending on project/assembly name)
- Several `.dll` files for the application and its dependencies
- `appsettings.json` (copied from the project; used for basic configuration)
- Any WPF resource assemblies or runtime folders created by the build

Treat the **entire `publish` folder** as the release unit:

- When deploying to another machine, **copy the whole folder** (for example to `C:\Apps\BackupVaultEncryptor-v1\`).
- You can optionally zip the folder for transport and unzip it on the operator machine.

### Running the published app

On the operator machine:

1. Ensure **.NET 8 Desktop Runtime (x64)** is installed (for the framework-dependent build).
2. Copy the **entire `publish` folder** to a chosen location (e.g. `C:\Apps\BackupVaultEncryptor-v1\`).
3. Open that folder in File Explorer.
4. Double-click `BackupVaultEncryptor.App.exe` to start the application.
5. (Optional) Create a desktop shortcut pointing to the `.exe` in that folder.

### Runtime data, settings, log, and database files

At runtime, the application creates and uses a simple local data folder relative to where the app is run:

- **App data directory** (default):
  - `AppData` folder next to the `.exe`
  - This can be changed via `AppDataDirectory` in `appsettings.json` if needed.

Inside this `AppData` folder, the app creates:

- **SQLite database file** (default name):
  - `appdata.db`
  - Stores local user accounts, vault master key metadata, and per-root key metadata.
- **Log file** (default name):
  - `app.log`
  - Contains simple timestamped log messages about app startup, authentication, backup operations, and errors.

These defaults are controlled by `appsettings.json` keys:

- `AppDataDirectory` (default `"AppData"`)
- `DatabaseFileName` (default `"appdata.db"`)
- `LogFileName` (default `"app.log"`)

If `AppDataDirectory` is a relative path (the default), it is resolved under the folder where the application is running. If you move the whole application folder (including the `AppData` subfolder), your users and vault metadata move with it.

### What to back up to preserve users and vault metadata

To preserve local users, authentication data, and vault key metadata for a given deployment:

1. Back up the **entire application folder**, including the `publish` contents.
2. Make sure the **`AppData` folder** (or the custom `AppDataDirectory` you configured) is included in that backup.
   - This ensures you keep:
     - `appdata.db` (SQLite database with users and keys)
     - `app.log` (log history)

Restoring from backup typically means restoring:

- The application folder (including the `publish` files), and
- The `AppData` folder (or your configured app data directory) with `appdata.db` and `app.log`.

### Deployment limitations (v1)

- The documented publish commands target **Windows x64 only** (`win-x64`).
- The recommended path is **framework-dependent**, which **requires .NET 8 Desktop Runtime (x64)** on the operator machine.
- The application relies on **Windows credentials** for SMB access; it does not manage SMB logins itself.
- All data (users, vault metadata, logs) are stored **locally** in the app data directory; copying just the `.exe` to a new location or machine does **not** bring over existing users or keys.
- For best performance on large SMB backups, run the app on a reasonably fast PC with good network connectivity to the backup share.