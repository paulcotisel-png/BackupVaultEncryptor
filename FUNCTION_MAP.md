# FUNCTION_MAP.md

## Current status
WPF project skeleton with initial configuration, logging, and local SQLite bootstrap wired into application startup.

## Areas
- UI: MainWindow with Main, Settings, Logs tabs
- Core/Infrastructure:
  - `AppConfig`
    - Properties: `AppDataDirectory`, `DatabaseFileName`, `LogFileName`
  - `AppConfigLoader`
    - `LoadConfiguration()`
      - Loads `appsettings.json` from the application directory
      - Binds values into `AppConfig`
      - Ensures simple defaults if values are missing
- Data:
  - `SqliteBootstrap`
    - `InitializeDatabase()`
      - Ensures the app data directory exists
      - Builds the SQLite database path from `AppConfig`
      - Opens/creates the SQLite database file
      - Creates a simple `Logs` table with `CREATE TABLE IF NOT EXISTS`
- Logging:
  - `AppLogger`
    - Constructor accepts `AppConfig` and resolves the log file path
    - `Log(string message)` writes timestamped lines to a text file
    - Maintains a small in-memory list of recent log entries
- Application startup:
  - `App` (in `App.xaml.cs`)
    - Overrides `OnStartup`
    - Loads configuration
    - Initializes `AppLogger`
    - Runs `SqliteBootstrap.InitializeDatabase()`
    - Writes simple startup log messages

- Security:
  - `PasswordHashOptions`
    - Holds tunable Argon2id parameters (memory, iterations, parallelism, sizes)
  - `PasswordHasher`
    - `HashPassword(string password)` returns encoded Argon2id hash string
    - `VerifyPassword(string password, string encodedHash)` verifies password against stored hash
  - `PasswordKeyDeriver`
    - `DeriveKey(string password, byte[] salt, int keySizeBytes, string purpose)` derives symmetric keys using Argon2id
  - `ILocalSecretProtector`
    - Abstraction for local secret protection
  - `DpapiLocalSecretProtector`
    - Uses Windows DPAPI (CurrentUser) to protect/unprotect secrets
  - `KeyWrapService`
    - `WrapWithKey(byte[] keyToWrap, byte[] wrappingKey)` wraps keys using AES-GCM
    - `UnwrapWithKey(WrappedKeyData wrapped, byte[] wrappingKey)` unwraps keys using AES-GCM
  - `WrappedKeyData`
    - Simple container for AES-GCM ciphertext, nonce, and tag

- Core Models:
  - `UserAccount`
    - Represents a local user with Argon2id password hash, wrapped VaultMasterKey (AES-GCM parts), DPAPI-protected VMK, key wrap salt, and recovery token fields
  - `RootKeyRecord`
    - Represents a RootKey per backup root, stored encrypted with VaultMasterKey (AES-GCM parts)
  - `BundleKeyRecord`
    - Placeholder model for future bundle key support (no DB or logic in v1 auth task)

- Data:
  - `SqliteBootstrap`
    - `InitializeDatabase()`
      - Ensures the app data directory exists
      - Builds the SQLite database path from `AppConfig`
      - Opens/creates the SQLite database file
      - Creates tables with `CREATE TABLE IF NOT EXISTS`:
        - `Logs` table for simple text log entries
        - `Users` table for local auth + vault master key fields
        - `RootKeys` table for per-backup-root keys
  - `UserRepository`
    - `GetByUsername(string username)` returns a `UserAccount` or null
    - `GetById(int id)` returns a `UserAccount` or null
    - `CreateUser(UserAccount user)` inserts and returns new Id
    - `UpdateUser(UserAccount user)` updates all user fields
  - `VaultKeyRepository`
    - `GetRootKey(int userId, string rootPath)` returns a `RootKeyRecord` or null
    - `GetRootKeysForUser(int userId)` returns all `RootKeyRecord` rows for the user
    - `AddRootKey(RootKeyRecord rootKey)` inserts and returns new Id

- Backup engine:
  - `BackupJobManifest`, `BackupBundleInfo`, `BackupBundleEntry`
    - Plaintext JSON manifest describing jobs, bundles, and entries
    - Stores source root, bundle list, relative paths, plaintext offsets/lengths
  - `FileScanner`
    - `IEnumerable<FileEntry> ScanFiles(string sourceRoot)` enumerates files with full and relative paths
  - `BundlePlanner`
    - `IEnumerable<PlannedBundle> PlanBundles(IEnumerable<FileEntry> files, long targetBundleSizeBytes)` groups files into bundles and assigns plaintext offsets
  - `BundleKeyDerivation`
    - `byte[] DeriveBundleKey(byte[] rootKey, string jobId, int bundleIndex)` deterministic HMAC-SHA256-based per-bundle key derivation
  - `ManifestStorage`
    - `Task SaveAsync(BackupJobManifest manifest, string manifestPath, CancellationToken ct)` writes manifest JSON
    - `Task<BackupJobManifest> LoadAsync(string manifestPath, CancellationToken ct)` reads manifest JSON
  - `BundleEncryptor`
    - `Task<BackupJobManifest> EncryptAsync(BackupJobEncryptRequest request, CancellationToken ct)` scans files, plans bundles, and writes chunked AES-GCM bundle files + manifest
  - `BundleDecryptor`
    - `Task DecryptAsync(BackupJobDecryptRequest request, CancellationToken ct)` reads manifest and restores original folder/file structure from bundles
- Services:
  - `VaultService`
    - `GetOrCreateRootKey(UserSession session, string rootPath)` creates or retrieves a `RootKeyRecord` for a backup root
    - `byte[] UnwrapRootKey(RootKeyRecord record, UserSession session)` unwraps and returns the RootKey using the session's VaultMasterKey

## Notes
This file will be updated by Cline whenever new classes, services, or important functions are added.
Keep entries concise and practical.
