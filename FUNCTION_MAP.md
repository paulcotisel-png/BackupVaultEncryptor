# FUNCTION_MAP.md

## Current status
WPF project skeleton with initial configuration, logging, and local SQLite bootstrap wired into application startup.

## Areas
- UI: MainWindow with Encrypt, Decrypt, Settings tabs and shared status/progress/log area
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
    - Composes core services (auth, vault, backup engine, settings)
    - Logs detailed startup milestones (config, logger, database, services, auth flow, main window)
    - Wraps startup in a top-level try/catch to log fatal errors and show a simple MessageBox with the exception type and message
    - Uses explicit `ShutdownMode` control so the app stays alive during modal auth dialogs and shuts down explicitly on auth failure/cancel or when the main window closes
  - `FirstUserSetupWindow` (in `FirstUserSetupWindow.xaml.cs`)
    - Constructor accepts `AuthService`, `AppState`, and `AppLogger`
    - `Create_Click` validates input, prevents double-submit with a simple `_isCreating` flag and disables the Create button during processing
    - On success: calls `AuthService.RegisterUser` then `AuthService.Login`, sets `AppState.CurrentSession`, sets `DialogResult = true`, and closes the window
    - On duplicate username: shows a beginner-friendly message advising to log in or choose another username
    - If registration succeeds but automatic login fails: shows a clear message, logs the issue, and closes the window so startup exits cleanly
    - On unexpected exceptions: logs the full exception details and shows a simple error message without exposing internal details

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
    - `Task<BackupJobManifest> EncryptAsync(BackupJobEncryptRequest request, IProgress<BackupProgress>? progress = null, CancellationToken ct = default)` scans files, plans bundles, writes chunked AES-GCM bundle files + manifest, and optionally reports byte-based progress across bundles
  - `BundleDecryptor`
    - `Task DecryptAsync(BackupJobDecryptRequest request, IProgress<BackupProgress>? progress = null, CancellationToken ct = default)` reads manifest and restores original folder/file structure from bundles, optionally reporting byte-based progress across bundles
  - `BackupProgress`
    - Simple model carrying `Phase`, `ProcessedBytes`, `TotalBytes`, `PercentComplete`, `CurrentBundleIndex`, `TotalBundles`, and optional `CurrentItemName` for UI progress display
- Services:
  - `AuthService`
    - `RegisterUser(string username, string password)` creates a new local user, hashes the password with Argon2id, generates and wraps a VaultMasterKey, and stores all fields in the `Users` table. Recovery email is no longer collected or used.
    - `Login(string username, string password)` looks up the user by username, verifies the password using the stored Argon2id hash, derives the wrapping key, unwraps the VaultMasterKey, and returns a `UserSession` on success. Logs each step of the login flow; returns `null` for bad credentials and throws `LoginInternalException` for post-verification internal failures.
    - `LoginInternalException` is a nested exception type used to signal that credentials were valid but an internal error occurred after verification (for example, during VMK unwrap or session creation). This allows the UI to show a different message than for bad credentials.
  - `VaultService`
    - `GetOrCreateRootKey(UserSession session, string rootPath)` creates or retrieves a `RootKeyRecord` for a backup root
    - `byte[] UnwrapRootKey(RootKeyRecord record, UserSession session)` unwraps and returns the RootKey using the session's VaultMasterKey

## Notes
This file will be updated by Cline whenever new classes, services, or important functions are added.
Keep entries concise and practical.
