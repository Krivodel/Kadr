namespace Kadr.Desktop.Services.Updates;

internal static class UpdateRestartScript
{
    internal static string Create(int processId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(processId);
        string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        return $"\"{powershell}\" -NoProfile -NonInteractive -WindowStyle Hidden -Command \"$p = Get-Process -Id {processId} -ErrorAction SilentlyContinue; if ($p -and (-not $p.WaitForExit(60000))) {{ exit 1 }}; exit 0\"\r\nif errorlevel 1 goto :ScriptFailed";
    }
}
