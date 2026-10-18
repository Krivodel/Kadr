using System.Text;

using Microsoft.Extensions.Logging;

namespace Kadr.Desktop.Services.Export;

internal static class ClipboardImageFile
{
    private static readonly string DirectoryPath = Path.Combine(Path.GetTempPath(), "Kadr", "Clipboard");

    internal static async Task<string> CreateAsync(byte[] bytes, CancellationToken ct)
    {
        Directory.CreateDirectory(DirectoryPath);
        string path = Path.Combine(DirectoryPath, $"Kadr-{Guid.NewGuid():N}.jpg");
        try
        {
            await File.WriteAllBytesAsync(path, bytes, ct).ConfigureAwait(false);
        }
        catch
        {
            File.Delete(path);
            throw;
        }
        return path;
    }

    internal static byte[] CreateDropList(string path)
    {
        byte[] names = Encoding.Unicode.GetBytes(path + "\0\0");
        byte[] data = new byte[20 + names.Length];
        BitConverter.GetBytes(20).CopyTo(data, 0);
        BitConverter.GetBytes(1).CopyTo(data, 16);
        names.CopyTo(data, 20);
        return data;
    }

    internal static void Finish(string path, bool copied, ILogger logger)
    {
        RemoveObsolete(copied ? path : null, logger, copied ? null : path);
    }

    internal static void RemoveObsolete(string? retained, ILogger logger, string? failed = null)
    {
        if (!Directory.Exists(DirectoryPath))
        {
            return;
        }
        IEnumerable<string> obsolete = failed is null
            ? Directory.EnumerateFiles(DirectoryPath, "Kadr-*.jpg").Where(candidate => candidate != retained)
            : new string[] { failed };
        foreach (string candidate in obsolete)
        {
            try
            {
                File.Delete(candidate);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(exception, "An obsolete clipboard payload could not be removed");
            }
        }
    }
}
