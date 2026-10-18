namespace Kadr.Desktop.Services.Settings;

public interface ISettingsApplication
{
    Task ApplyAsync(AppSettings settings, CancellationToken ct);
}
