using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BackupVaultEncryptor.App.Core.Models;

namespace BackupVaultEncryptor.App.Core.BackupEngine
{
    /// <summary>
    /// Simple JSON-based storage helper for BackupJobManifest.
    /// </summary>
    public class ManifestStorage
    {
        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        public async Task SaveAsync(BackupJobManifest manifest, string manifestPath, CancellationToken cancellationToken)
        {
            var directory = Path.GetDirectoryName(manifestPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var stream = new FileStream(manifestPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await JsonSerializer.SerializeAsync(stream, manifest, SerializerOptions, cancellationToken);
        }

        public async Task<BackupJobManifest> LoadAsync(string manifestPath, CancellationToken cancellationToken)
        {
            await using var stream = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var manifest = await JsonSerializer.DeserializeAsync<BackupJobManifest>(stream, SerializerOptions, cancellationToken);
            return manifest ?? new BackupJobManifest();
        }
    }
}
