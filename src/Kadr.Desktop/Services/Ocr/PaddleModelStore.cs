using System.Security.Cryptography;
using System.Text.Json;

namespace Kadr.Desktop.Services.Ocr;

public sealed class PaddleModelStore
{
    public string DirectoryPath { get; }

    private readonly IReadOnlyList<PaddleModelFile> _files;

    public PaddleModelStore()
        : this(ApplicationDataPaths.Default.OcrDirectory)
    {
    }

    public PaddleModelStore(string directoryPath)
    {
        ArgumentNullException.ThrowIfNull(directoryPath);
        DirectoryPath = Path.Combine(directoryPath, "paddle-v5");
        _files = ReadFiles();
    }

    internal async Task EnsureAvailableAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(DirectoryPath);
        foreach (PaddleModelFile model in _files)
        {
            string path = Path.Combine(DirectoryPath, model.Name);
            if (File.Exists(path))
            {
                using FileStream existing = File.OpenRead(path);
                byte[] hash = await SHA256.HashDataAsync(existing, ct).ConfigureAwait(false);
                if (Convert.ToHexString(hash) == model.Checksum)
                {
                    continue;
                }
            }

            using Stream resource = typeof(PaddleModelStore).Assembly.GetManifestResourceStream(model.ResourceName)
                ?? throw new InvalidDataException("A bundled PaddleOCR model is missing.");
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (FileStream output = File.Create(temporary))
                {
                    await resource.CopyToAsync(output, ct).ConfigureAwait(false);
                }

                using (FileStream output = File.OpenRead(temporary))
                {
                    byte[] hash = await SHA256.HashDataAsync(output, ct).ConfigureAwait(false);
                    if (Convert.ToHexString(hash) != model.Checksum)
                    {
                        throw new InvalidDataException("A bundled PaddleOCR model checksum is invalid.");
                    }
                }

                File.Move(temporary, path, true);
            }
            finally
            {
                File.Delete(temporary);
            }
        }
    }

    private static IReadOnlyList<PaddleModelFile> ReadFiles()
    {
        using Stream resource = typeof(PaddleModelStore).Assembly.GetManifestResourceStream("Kadr.Desktop.Resources.paddle-models.json")
            ?? throw new InvalidDataException("The PaddleOCR model manifest is missing.");
        Dictionary<string, string> checksums = JsonSerializer.Deserialize<Dictionary<string, string>>(resource)
            ?? throw new InvalidDataException("The PaddleOCR model manifest is invalid.");
        IEnumerable<PaddleModelFile> files = checksums.Select(file => new PaddleModelFile(file.Key, "Kadr.Paddle." + file.Key, file.Value));

        return files.ToArray();
    }
}
