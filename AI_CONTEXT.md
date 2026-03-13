# AI_CONTEXT.md

## Project
Windows desktop app for encrypting and decrypting folders/files, with the primary use case being backup encryption on SMB shares.

## Main real-world use case
80% of usage is encrypting backup root folders located on an SMB server.
20% of usage is normal local folder/file encryption and decryption.

The app runs from a Windows PC.
SMB authentication code is NOT needed.
Windows credentials already provide access to SMB locations.

## Backup structure
Typical source is a PC backup root folder named after the PC, for example:
\\server\backups\PCNAME\

Contents usually include:
- Desktop
- drive folders except C:
- many thousands of Word, Excel, PDF, and PowerPoint files
- average file size around 1.5 MB
- total backup size can vary from under 1 GB to over 200 GB

## Performance priorities
Optimize for:
- sequential I/O
- low SMB chatter
- low NAS load
- resumable processing
- safe authenticated encryption
- stable operation over 1 Gbps network
- HDD-backed NAS targets

Avoid:
- high default concurrency
- per-file crypto overhead for huge file counts
- unnecessary compression
- unnecessary whole-repo complexity

## Chosen v1 architecture
- Language: C#
- Runtime: .NET 8
- Desktop UI: WPF
- Local database: SQLite
- Logging: structured local logs + UI log viewer
- Crypto payload design: bundled streaming encryption
- Vault design: random VaultMasterKey, RootKeys per PC backup root, BundleKey per encrypted bundle
- Password hashing: Argon2id
- Local sensitive secret protection: DPAPI
- Email password recovery: token-based reset by email
- No separate backend/server in v1

## Encryption model
- Group source files into bundle files with a target max size of 4 GiB
- Encrypt bundles in streaming mode
- Default chunk size is 8 MiB
- Default worker count is 1
- Compression disabled in v1
- Preserve relative path metadata in encrypted manifest
- Support resume/retry for interrupted jobs
- Support decrypting back to original folder structure

## Key model
- Each PC root folder gets a unique RootKey
- Each encrypted bundle gets a random BundleKey
- BundleKey is wrapped by RootKey
- RootKey is stored in the vault
- Vault content is encrypted by VaultMasterKey
- VaultMasterKey is wrapped by a key derived from the user password
- Password reset must re-wrap VaultMasterKey and must NOT make old data undecryptable

## Authentication and recovery
- App has username/password login
- Passwords are stored only as Argon2id hashes
- Email confirmation/recovery settings are configurable
- Recovery sends reset tokens only, never keys
- SMTP provider configuration lives in app settings, not hardcoded

## UI requirements
Main window:
- Encrypt / Decrypt mode selector
- Source location picker
- Destination location picker
- Start action
- Progress feedback
- Cancel if practical
- Clear status messages

Settings tab:
- bundle size
- chunk size
- worker count
- SMTP settings
- default destinations
- vault/session settings
- anything else needed for safe operation

Logs tab:
- view job logs
- filter/search
- export or copy useful errors

All path inputs must support Windows folder/file pickers.

## Coding rules
- Prefer simple, explicit architecture over abstraction-heavy design
- MVVM pattern is acceptable but keep it beginner-readable
- No hardcoded secrets
- No insecure crypto fallback
- Keep patches minimal
- Keep files reasonably small
- Add comments only where they explain non-obvious security or format decisions

## Token efficiency rules for Cline
- Always read this file first in a new task
- Ask for a plan before editing
- List exact files/functions needed
- Avoid reading unrelated files
- Prefer function-level changes
- Return minimal diffs
- Do not dump long logs
- Do not rewrite entire files unless necessary

## Initial deliverables expected across the project
- runnable WPF app
- local auth
- vault/key management
- bundle encryption/decryption engine
- SMB/local path support
- settings UI
- logs UI
- recovery email flow
- packaging/build instructions
- concise docs for use and recovery
