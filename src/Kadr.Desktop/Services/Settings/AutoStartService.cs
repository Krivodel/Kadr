using Microsoft.Win32;

namespace Kadr.Desktop.Services.Settings;

public sealed class AutoStartService
{
    public void SetEnabled(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");

        if (enabled)
        {
            string executable = Environment.ProcessPath ?? throw new InvalidOperationException("The application executable is unavailable.");
            key.SetValue("Kadr", $"\"{executable}\" --silent");
        }
        else
        {
            key.DeleteValue("Kadr", false);
        }
    }
}
