# VaultEncrypt MSI Installer

This document describes how to build and use the MSI installer for VaultEncrypt.

The installer:
- Installs **per machine** under `C:\Program Files\VaultEncrypt` (x64).
- Creates a **Start Menu** shortcut and an optional **Desktop** shortcut.
- Adds an entry to **Apps & Features / Control Panel**.
- Supports clean **uninstall** and **major upgrades** (newer MSI replaces older one).
- **Does not** remove user data stored in per-user LocalAppData.

## 1. Build self-contained installer payload

The MSI uses a self-contained `win-x64` publish of the WPF app as its payload.

From the repository root (`c:\PythonProjects\BackupVaultEncryptor`), run:

```powershell
dotnet publish .\src\BackupVaultEncryptor.App\BackupVaultEncryptor.App.csproj /p:PublishProfile=Win-x64-Installer
```

This uses the `Win-x64-Installer.pubxml` profile, which is configured to:
- Target `net8.0-windows`.
- Use `RuntimeIdentifier = win-x64`.
- Set `SelfContained = true`.
- Set `PublishSingleFile = false`.
- Set `PublishTrimmed = false`.
- Use a predictable publish directory:
  - `src\BackupVaultEncryptor.App\bin\Release\net8.0-windows\win-x64\publish-installer\`

All files in that directory, **including `appsettings.json`**, are packaged into the MSI unchanged.

## 2. Build the MSI installer

After publishing, build the WiX installer project:

```powershell
dotnet build .\src\BackupVaultEncryptor.Installer\BackupVaultEncryptor.Installer.wixproj -c Release
```

The resulting MSI will be created under:

- `src\BackupVaultEncryptor.Installer\bin\Release\`

with a file name similar to `BackupVaultEncryptor.msi`.

## 3. Install the MSI

1. Open File Explorer and navigate to:
   - `src\BackupVaultEncryptor.Installer\bin\Release\`
2. Double-click `BackupVaultEncryptor.msi`.
3. Follow the standard Windows Installer UI to complete installation (administrator rights may be required).

After install:
- The app is installed under `C:\Program Files\VaultEncrypt`.
- A Start Menu shortcut is created in the **VaultEncrypt** folder.
- A Desktop shortcut named **VaultEncrypt** is created for all users.

## 4. Uninstall the MSI

**Primary method (recommended):**

1. Open **Apps & Features** (or **Programs and Features** in classic Control Panel).
2. Locate **BackupVaultEncryptor** in the list of installed apps.
3. Click **Uninstall** and follow the prompts.

**Advanced / admin method (optional):**

You can also uninstall from an elevated PowerShell using `msiexec` and the product code GUID:

```powershell
msiexec /x "{PRODUCT-CODE-GUID-HERE}"
```

Use this form only if you need scripted or remote uninstalls; for normal usage, prefer Apps & Features.

## 5. Data retention behavior

The MSI only manages files under:
- `C:\Program Files\BackupVaultEncryptor` (installed binaries)
- Start Menu and Desktop shortcuts

It **does not** create or manage components under per-user LocalAppData.

Therefore:
- On uninstall, the MSI removes **only** the installed binaries and shortcuts.
- User data under LocalAppData (for example, vault data, SQLite database, logs) is **not** deleted.

If you ever need to fully erase local data, you must manually remove the application data directory used by the app (as defined by `AppConfig.AppDataDirectory`).

## 6. Major upgrades

The MSI is configured for **major upgrades**:
- All versions share a common `UpgradeCode`.
- Installing a newer MSI with a higher product version automatically:
  - Uninstalls the previous version's binaries and shortcuts.
  - Installs the new version in their place.
- LocalAppData is **not** touched during upgrades, so user data is preserved.

## 7. Versioning

- App project version (`Version` in `BackupVaultEncryptor.App.csproj`): 3-part (e.g. `2.0.0`).
- AssemblyVersion / FileVersion: 4-part (e.g. `2.0.0.0`).
- MSI `ProductVersion` (in `Product.wxs`): 3-part (e.g. `2.0.0`).

The MSI product version should always match the app project `Version`. When you bump the app version, update the MSI `Version` in `Product.wxs` to the same 3-part value.

## 8. Code signing (future recommendation)

The MSI produced by this setup is **unsigned**. It is suitable for internal and test use, but Windows may display additional warnings during installation.

For smoother installs across many PCs or for broader distribution, it is recommended to:
- Obtain a code-signing certificate.
- Use `signtool.exe` (from the Windows SDK) to sign the MSI, and optionally the main EXE.

Code signing is **not** required or implemented in this project by default.
