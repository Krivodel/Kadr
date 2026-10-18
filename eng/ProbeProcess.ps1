function New-ProbeProcess(
    [string]$Executable,
    [string[]]$Arguments,
    [string]$WorkingDirectory,
    [hashtable]$ProcessEnvironment = @{}
) {
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo.FileName = $Executable
    $process.StartInfo.Arguments = $Arguments -join ' '
    $process.StartInfo.WorkingDirectory = $WorkingDirectory
    $process.StartInfo.UseShellExecute = $false
    $process.StartInfo.CreateNoWindow = $true
    $process.StartInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $process.StartInfo.RedirectStandardOutput = $true
    $process.StartInfo.RedirectStandardError = $true
    $process.StartInfo.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
    $process.StartInfo.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
    foreach ($name in $ProcessEnvironment.Keys) {
        $process.StartInfo.EnvironmentVariables[$name] = $ProcessEnvironment[$name]
    }
    return $process
}
