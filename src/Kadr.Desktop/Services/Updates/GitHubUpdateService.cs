using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;

using Octokit;
using StageKit.Updatum;

namespace Kadr.Desktop.Services.Updates;

public sealed class GitHubUpdateService : IUpdateService, IDisposable
{
    public const string AssetName = "Kadr-win-x64.exe";

    private readonly UpdatumManager _manager;
    private readonly HttpClient _http;
    private readonly byte[] _publicKey;
    private Release? _release;
    private UpdatumDownloadedAsset? _download;
    private IProgress<UpdateProgress>? _progress;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _installStarted;

    public GitHubUpdateService(HttpClient http)
    {
        _http = http;
        using Stream key = typeof(GitHubUpdateService).Assembly.GetManifestResourceStream("Kadr.UpdatePublicKey")
            ?? throw new InvalidOperationException("The update verification key is missing.");
        using StreamReader reader = new(key);
        _publicKey = Convert.FromBase64String(reader.ReadToEnd().Trim());
        _manager = new UpdatumManager("Krivodel", "Kadr", typeof(GitHubUpdateService).Assembly.GetName().Version)
        {
            AssetRegexPattern = "^Kadr-win-x64\\.exe$",
            AssetExtensionFilter = ".exe",
            AssetHttpClient = http,
            RequireAssetChecksum = true,
            RequireAssetSignatureVerification = true,
            AssetSignatureVerifier = VerifyAsync,
            InstallUpdateWindowsExeType = UpdatumWindowsExeType.SingleFileApp,
            InstallUpdateSingleFileExecutableNameStrategy = UpdatumSingleFileExecutableNameStrategy.CustomName,
            InstallUpdateSingleFileExecutableName = Path.GetFileNameWithoutExtension(Environment.ProcessPath)!.Replace("{", "{{").Replace("}", "}}"),
            EventSynchronizationContext = null
        };
        _manager.PropertyChanged += OnProgressChanged;
    }

    public async Task<UpdateRelease?> CheckAsync(CancellationToken ct)
    {
        if (!await _gate.WaitAsync(0, ct).ConfigureAwait(false))
        {
            return _release is null ? null : new UpdateRelease(GetVersion(_release));
        }
        try
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            await _manager.CheckForUpdatesAsync(timeout.Token).ConfigureAwait(false);
            Release? release = _manager.LatestRelease;
            if (release is null || _manager.GetCompatibleReleaseAsset(release) is null
                || !release.Assets.Any(asset => asset.Name == AssetName + ".sig"))
            {
                _release = null;
                return null;
            }
            _release = release;
            return new UpdateRelease(GetVersion(release));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DownloadAsync(IProgress<UpdateProgress> progress, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Release release = _release ?? throw new InvalidOperationException("No update is selected.");
            _progress = progress;
            _download?.SafeDeleteFile();
            _download = null;
            _download = await _manager.DownloadUpdateAsync(release, ct).ConfigureAwait(false)
                ?? throw new IOException("The update could not be downloaded.");
        }
        finally
        {
            _progress = null;
            _gate.Release();
        }
    }

    [UnconditionalSuppressMessage("SingleFile", "IL3000", Justification = "Assembly.Location is intentionally used to reject updates in unpackaged development builds.")]
    public async Task InstallAsync(string sessionId, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!Guid.TryParseExact(sessionId, "N", out _))
            {
                throw new ArgumentException("Invalid session identifier.", nameof(sessionId));
            }
            if (typeof(GitHubUpdateService).Assembly.Location.Length != 0)
            {
                throw new InvalidOperationException("Updates require the published single-file application.");
            }
            string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Executable path is missing.");
            string checkPath = Path.Combine(Path.GetDirectoryName(executable)!, ".kadr-write-" + Guid.NewGuid().ToString("N"));
            using (FileStream check = new(checkPath, System.IO.FileMode.CreateNew, System.IO.FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
                check.WriteByte(0);
            }
            _manager.InstallUpdateInjectCustomScript = UpdateRestartScript.Create(Environment.ProcessId);
            bool started = await _manager.InstallUpdateAsync(_download ?? throw new InvalidOperationException("No verified update is ready."),
                false, "--resume-update " + sessionId, ct).ConfigureAwait(false);
            if (!started)
            {
                throw new IOException("The update installer could not be started.");
            }
            _installStarted = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async ValueTask<bool> VerifyAsync(string path, CancellationToken ct)
    {
        _progress?.Report(new UpdateProgress(100, true));
        Release release = _release ?? throw new InvalidOperationException("No update is selected.");
        ReleaseAsset signature = release.Assets.Single(asset => asset.Name == AssetName + ".sig");
        using HttpResponseMessage response = await _http.GetAsync(signature.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 16384)
        {
            return false;
        }
        await using Stream input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        byte[] buffer = new byte[16385];
        int count = 0;
        while (count < buffer.Length)
        {
            int read = await input.ReadAsync(buffer.AsMemory(count), ct).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }
            count += read;
        }
        if (count == buffer.Length)
        {
            return false;
        }
        UpdateSignatureManifest? manifest = JsonSerializer.Deserialize<UpdateSignatureManifest>(buffer.AsSpan(0, count));
        await using FileStream file = File.OpenRead(path);
        string hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, ct).ConfigureAwait(false));
        return manifest is not null && UpdateSignature.Verify(manifest, GetVersion(release), AssetName, hash, _publicKey);
    }

    private static string GetVersion(Release release)
    {
        return Version.Parse(release.TagName.TrimStart('v', 'V')).ToString();
    }

    private void OnProgressChanged(object? sender, PropertyChangedEventArgs arguments)
    {
        if (arguments.PropertyName == nameof(UpdatumManager.DownloadedPercentage))
        {
            _progress?.Report(new UpdateProgress(Math.Clamp(_manager.DownloadedPercentage, 0, 100)));
        }
    }

    public void Dispose()
    {
        _manager.PropertyChanged -= OnProgressChanged;
        if (!_installStarted)
        {
            _download?.SafeDeleteFile();
        }
        _manager.Dispose();
        _gate.Dispose();
    }
}
