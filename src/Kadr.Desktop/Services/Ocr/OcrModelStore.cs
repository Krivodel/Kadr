using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Kadr.Desktop.Services.Ocr;

public sealed class OcrModelStore
{
    public OcrManifest Manifest
    {
        get;
    }
    public string DirectoryPath
    {
        get;
    }

    private static readonly Assembly BundledAssembly = typeof(OcrModelStore).Assembly;
    private readonly HttpClient _http;

    public OcrModelStore(HttpClient http)
        : this(http, ApplicationDataPaths.Default.OcrDirectory)
    {
    }

    public OcrModelStore(HttpClient http, string directoryPath)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        using Stream manifest = BundledAssembly.GetManifestResourceStream("Kadr.Desktop.Resources.ocr-languages.json")
            ?? throw new InvalidOperationException("The OCR language manifest is missing.");
        Manifest = JsonSerializer.Deserialize<OcrManifest>(manifest)
            ?? throw new InvalidOperationException("The OCR language manifest is invalid.");
        DirectoryPath = directoryPath ?? throw new ArgumentNullException(nameof(directoryPath));
    }

    public static void Verify(byte[] bytes, OcrLanguage language)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(language);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        hash.AppendData(Encoding.UTF8.GetBytes($"blob {bytes.Length}\0"));
        hash.AppendData(bytes);
        string actual = Convert.ToHexString(hash.GetHashAndReset());

        if ((bytes.LongLength != language.Size) || !string.Equals(actual, language.Sha1, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The OCR model does not match the pinned Git blob checksum.");
        }
    }

    public bool IsInstalled(string code)
    {
        return File.Exists(Path.Combine(DirectoryPath, code + ".traineddata"))
            || BundledAssembly.GetManifestResourceInfo(GetBundledResourceName(code)) is not null;
    }

    public async Task InstallAsync(string code, CancellationToken ct)
    {
        OcrLanguage language = Manifest.Languages.Single(value => string.Equals(value.Code, code, StringComparison.Ordinal));
        Directory.CreateDirectory(DirectoryPath);
        string target = Path.Combine(DirectoryPath, code + ".traineddata");
        using Stream? bundled = BundledAssembly.GetManifestResourceStream(GetBundledResourceName(code));
        byte[] bytes;
        if (bundled is not null)
        {
            using MemoryStream buffer = new();
            await bundled.CopyToAsync(buffer, ct).ConfigureAwait(false);
            bytes = buffer.ToArray();
        }
        else
        {
            bytes = await _http.GetByteArrayAsync($"https://raw.githubusercontent.com/tesseract-ocr/tessdata_best/{Manifest.Commit}/{code}.traineddata", ct).ConfigureAwait(false);
        }
        Verify(bytes, language);
        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, ct).ConfigureAwait(false);
            File.Move(temporary, target, true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static string GetBundledResourceName(string code)
    {
        return $"Kadr.Ocr.{code}.traineddata";
    }

}
