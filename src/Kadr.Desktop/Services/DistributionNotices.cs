using System.Reflection;

namespace Kadr.Desktop.Services;

internal static class DistributionNotices
{
    internal static async Task ExportAsync(string directory, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        const string Prefix = "Kadr.Licenses.";
        Assembly assembly = typeof(DistributionNotices).Assembly;
        Directory.CreateDirectory(directory);

        foreach (string name in assembly.GetManifestResourceNames().Where(name => name.StartsWith(Prefix, StringComparison.Ordinal)))
        {
            using Stream resource = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"The bundled license resource {name} is missing.");
            await using FileStream output = new(Path.Combine(directory, name[Prefix.Length..]), FileMode.Create, FileAccess.Write, FileShare.None);
            await resource.CopyToAsync(output, ct).ConfigureAwait(false);
        }
    }
}
