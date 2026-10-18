using System.Security.Cryptography;
using System.Text.Json;

namespace Kadr.Desktop.Services.Updates;

public sealed class UpdateSessionStore
{
    private readonly string _directory;

    public UpdateSessionStore(string? directory = null)
    {
        _directory = directory ?? ApplicationDataPaths.Default.UpdateSessionDirectory;
    }

    public async Task<string> SaveAsync(EditorSessionSnapshot snapshot, CancellationToken ct)
    {
        Directory.CreateDirectory(_directory);
        string id = Guid.NewGuid().ToString("N");
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(snapshot);
        byte[] encrypted;
        try
        {
            encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
        string temporary = Path.Combine(_directory, id + ".tmp");
        bool published = false;
        try
        {
            await File.WriteAllBytesAsync(temporary, encrypted, ct).ConfigureAwait(false);
            File.Move(temporary, GetPath(id));
            await File.WriteAllTextAsync(Path.Combine(_directory, "pending.tmp"), id, ct).ConfigureAwait(false);
            File.Move(Path.Combine(_directory, "pending.tmp"), Path.Combine(_directory, "pending"), true);
            published = true;
            return id;
        }
        finally
        {
            File.Delete(temporary);
            File.Delete(Path.Combine(_directory, "pending.tmp"));
            if (!published)
            {
                File.Delete(GetPath(id));
            }
        }
    }

    public async Task<string?> GetPendingAsync(CancellationToken ct)
    {
        string path = Path.Combine(_directory, "pending");
        if (!File.Exists(path))
        {
            return null;
        }
        string id = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        _ = GetPath(id);
        return id;
    }

    public async Task<EditorSessionSnapshot> LoadAsync(string id, CancellationToken ct)
    {
        byte[] encrypted = await File.ReadAllBytesAsync(GetPath(id), ct).ConfigureAwait(false);
        byte[] plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
        try
        {
            return JsonSerializer.Deserialize<EditorSessionSnapshot>(plain) ?? throw new InvalidDataException("Empty editor session.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    public void Complete(string id)
    {
        File.Delete(GetPath(id));
        File.Delete(Path.Combine(_directory, "pending"));
    }

    private string GetPath(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _))
        {
            throw new InvalidDataException("Invalid editor session identifier.");
        }
        return Path.Combine(_directory, id + ".session");
    }
}
