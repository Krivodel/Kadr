namespace Kadr.Desktop.Services.Export;

public interface IFilePicker
{
    Task<SaveDestination?> ChooseSaveDestinationAsync(string? directory, string format, CancellationToken ct);
}
