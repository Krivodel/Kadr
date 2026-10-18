using Avalonia.Controls;
using Avalonia.Platform.Storage;

using Kadr.Desktop.Resources;

namespace Kadr.Desktop.Services.Export;

internal sealed class AvaloniaFilePicker : IFilePicker
{
    private readonly Func<TopLevel?> _owner;

    internal AvaloniaFilePicker(Func<TopLevel?> owner)
    {
        _owner = owner;
    }

    public async Task<SaveDestination?> ChooseSaveDestinationAsync(string? directory, string format, CancellationToken ct)
    {
        TopLevel owner = _owner() ?? throw new InvalidOperationException("A save dialog requires an editor window.");
        FilePickerFileType png = new("PNG")
        {
            Patterns = new string[] { "*.png" }
        };
        FilePickerFileType jpeg = new("JPEG")
        {
            Patterns = new string[] { "*.jpg", "*.jpeg" }
        };
        IStorageFolder? folder = directory is not null ? await owner.StorageProvider.TryGetFolderFromPathAsync(directory) : null;
        IStorageFile? file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = UiText.Get("Kadr.SaveScreenshot"),
            SuggestedFileName = "Kadr_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"),
            DefaultExtension = format == "jpeg" ? "jpg" : "png",
            SuggestedStartLocation = folder,
            FileTypeChoices = format == "jpeg" ? new FilePickerFileType[] { jpeg, png } : new FilePickerFileType[] { png, jpeg },
            ShowOverwritePrompt = true
        });
        string? path = file?.TryGetLocalPath();

        if (path is null)
        {
            return null;
        }

        string extension = Path.GetExtension(path);
        return new SaveDestination(path, extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ? "jpeg" : "png");
    }
}
