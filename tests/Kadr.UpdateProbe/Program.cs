using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;

using Kadr.Desktop.Services.Editing;
using Kadr.Desktop.Services.Export;
using Kadr.Desktop.Services.Geometry;
using Kadr.Desktop.Services.Rendering;
using Kadr.Desktop.Services.Settings;
using Kadr.Desktop.Services.Updates;
using Kadr.Desktop.Tests;
using Kadr.Desktop.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Octokit;
using SkiaSharp;
using StageKit.Updatum;

namespace Kadr.UpdateProbe;

internal static class Program
{
    internal static async Task<int> Main(string[] args)
    {
        try
        {
            if (args is ["--resume-update", string id, string restoredDirectory])
            {
                UpdateSessionStore store = new(Path.Combine(restoredDirectory, "session"));
                EditorSessionSnapshot snapshot = await store.LoadAsync(id, CancellationToken.None);
                using EditorViewModel editor = CreateEditor(new DrawingDocument(snapshot.CreateDesktop(), false));
                snapshot.Restore(editor);
                using SKBitmap pixels = editor.Renderer.Export();
                string actual = Convert.ToHexStringLower(SHA256.HashData(pixels.Bytes));
                string expected = await File.ReadAllTextAsync(Path.Combine(restoredDirectory, "expected.txt"));
                if (actual != expected || !editor.History.CanUndo || !editor.History.CanRedo || editor.Interaction.SelectedId is null)
                {
                    throw new InvalidDataException("The restarted session differs from the original.");
                }
                store.Complete(id);
                await File.WriteAllTextAsync(Path.Combine(restoredDirectory, "result.json"), JsonSerializer.Serialize(new
                {
                    Passed = true, Executable = Environment.ProcessPath, PixelsMatch = true, HistoryRestored = true,
                    Version = typeof(Program).Assembly.GetName().Version!.ToString(), ProcessId = Environment.ProcessId
                }));
                return 0;
            }
            if (args is not ["install", string nextFile, string directory])
            {
                throw new ArgumentException("Use install <next single-file EXE> <isolated result directory>.");
            }
            string processPath = Path.GetFullPath(Environment.ProcessPath!);
            if (!processPath.StartsWith(Path.GetFullPath(directory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The probe must run from its isolated result directory.");
            }
            UpdateSessionStore sessions = new(Path.Combine(directory, "session"));
            using EditorViewModel original = CreateEditor(TestDocuments.Create(320, 240, SKColors.LightGray));
            DrawingElement arrow = TestDocuments.Element(DrawingToolId.Arrow, 30, 30, 250, 180) with { Bend = new PixelPoint(180, 30) };
            original.Document.SetElements(ImmutableArray.Create(arrow));
            original.History.Commit(ImmutableArray<DrawingElement>.Empty);
            ImmutableArray<DrawingElement> first = original.Document.Elements;
            original.Document.SetElements(first.Add(TestDocuments.Element(DrawingToolId.Blur, 40, 40, 80, 90)));
            original.History.Commit(first);
            original.History.Undo();
            original.Document.SetSelection(new PixelRectangle(5, 8, 280, 210));
            original.Interaction.RestoreSelection(arrow.Id);
            using SKBitmap expectedImage = original.Renderer.Export();
            await File.WriteAllTextAsync(Path.Combine(directory, "expected.txt"), Convert.ToHexStringLower(SHA256.HashData(expectedImage.Bytes)));
            string sessionId = await sessions.SaveAsync(EditorSessionSnapshot.Capture(original), CancellationToken.None);
            ReleaseAsset asset = new("https://example.test/update", 1, "node", "Kadr-win-x64.exe", "", "uploaded", "application/octet-stream",
                checked((int)new FileInfo(nextFile).Length), 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "https://example.test/update", null!);
            Release release = new("https://example.test/release", "https://example.test/release", "https://example.test/assets", "https://example.test/upload", 1,
                "node", "v1.1.0", "main", "Update probe", "", false, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null!,
                "https://example.test/source.tar.gz", "https://example.test/source.zip", new ReleaseAsset[] { asset });
            using UpdatumManager manager = new("local", "probe")
            {
                InstallUpdateWindowsExeType = UpdatumWindowsExeType.SingleFileApp,
                InstallUpdateSingleFileExecutableNameStrategy = UpdatumSingleFileExecutableNameStrategy.CustomName,
                InstallUpdateSingleFileExecutableName = Path.GetFileNameWithoutExtension(processPath).Replace("{", "{{").Replace("}", "}}"),
                InstallUpdateInjectCustomScript = UpdateRestartScript.Create(Environment.ProcessId)
            };
            // This probe exercises installation of a local fixture, never the production release trust policy.
            bool started = await manager.InstallUpdateAsync(new UpdatumDownloadedAsset(release, asset, nextFile), false,
                "--resume-update " + sessionId + " \"" + directory + "\"", CancellationToken.None);
            if (!started)
            {
                throw new IOException("The library did not start the replacement helper.");
            }
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            if (args.Length >= 3 && Directory.Exists(args[^1]))
            {
                await File.WriteAllTextAsync(Path.Combine(args[^1], "failure.txt"), exception.ToString());
            }
            return 1;
        }
    }

    private static EditorViewModel CreateEditor(DrawingDocument document)
    {
        EditorTestServices services = new();
        return new EditorViewModel(document, new DrawingToolRegistry(), services, services, new ImageFileWriter(), services,
            new SettingsStore(NullLogger<SettingsStore>.Instance, Path.Combine(Path.GetTempPath(), "Kadr-update-probe-settings.json")),
            NullLogger<EditorViewModel>.Instance);
    }
}
