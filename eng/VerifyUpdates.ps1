param([switch]$KeepFiles)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$dotnet = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
if (!(Test-Path -LiteralPath $dotnet)) { $dotnet = 'dotnet' }
$probeProject = Join-Path $projectRoot 'tests\Kadr.UpdateProbe\Kadr.UpdateProbe.csproj'
$runRoot = Join-Path $projectRoot ('artifacts\update-install-' + [guid]::NewGuid().ToString('N'))
$oldBuild = Join-Path $runRoot 'old'
$newBuild = Join-Path $runRoot 'new'
$working = Join-Path $runRoot 'Проверка & portable'
New-Item -ItemType Directory -Path $working -Force | Out-Null
$passed = $false
try {
    & $dotnet publish $probeProject -c Release -o $oldBuild -p:Version=1.0.0 -p:PublishReadyToRun=false -p:DebugType=embedded -v quiet
    if ($LASTEXITCODE -ne 0) { throw 'Publishing the old update fixture failed.' }
    & $dotnet publish $probeProject -c Release -o $newBuild -p:Version=1.1.0 -p:PublishReadyToRun=false -p:DebugType=embedded -v quiet
    if ($LASTEXITCODE -ne 0) { throw 'Publishing the new update fixture failed.' }
    $executable = Join-Path $working 'Мой кадр.exe'
    $nextFile = Join-Path $working 'next.exe'
    Copy-Item -LiteralPath (Join-Path $oldBuild 'Kadr.UpdateProbe.exe') -Destination $executable
    Copy-Item -LiteralPath (Join-Path $newBuild 'Kadr.UpdateProbe.exe') -Destination $nextFile
    $oldHash = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash
    $newHash = (Get-FileHash -LiteralPath $nextFile -Algorithm SHA256).Hash
    if ($oldHash -eq $newHash) { throw 'The fixture versions must differ.' }
    $stdout = Join-Path $working 'stdout.txt'
    $stderr = Join-Path $working 'stderr.txt'
    $process = Start-Process -FilePath $executable -ArgumentList @('install', ('"' + $nextFile + '"'), ('"' + $working + '"')) -PassThru -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    if (!$process.WaitForExit(30000)) { throw 'The old fixture did not exit.' }
    if ($process.ExitCode -ne 0) { throw "The old fixture failed; inspect $stderr." }
    $resultPath = Join-Path $working 'result.json'
    $failurePath = Join-Path $working 'failure.txt'
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    while (!(Test-Path -LiteralPath $resultPath) -and !(Test-Path -LiteralPath $failurePath) -and [DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 100
    }
    if (!(Test-Path -LiteralPath $resultPath)) { throw "The restarted fixture did not restore its session. Inspect $working." }
    $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    $restarted = Get-Process -Id $result.ProcessId -ErrorAction SilentlyContinue
    if ($restarted -and !$restarted.WaitForExit(10000)) { throw 'The restored fixture did not exit.' }
    if (!$result.Passed -or $result.Version -ne '1.1.0.0' -or $result.ProcessId -eq $process.Id -or $result.Executable -ne $executable) {
        throw 'The restart result does not match the expected executable or version.'
    }
    if ((Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash -ne $newHash) { throw 'The running executable was not replaced.' }
    $report = [ordered]@{
        Passed = $true
        OldVersion = '1.0.0'
        NewVersion = '1.1.0'
        ExecutableReplaced = $true
        RenamedExecutablePreserved = $true
        UnicodeSpacesAndAmpersand = $true
        FrozenPixelsMatch = $result.PixelsMatch
        HistoryRestored = $result.HistoryRestored
        VerifiedAt = [DateTime]::UtcNow.ToString('O')
    }
    $report | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $projectRoot 'artifacts\update-install-verification.json') -Encoding utf8
    $passed = $true
    $report
}
finally {
    if ($passed -and !$KeepFiles) {
        $resolvedRoot = (Resolve-Path -LiteralPath $runRoot).ProviderPath
        $artifactRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts')) + [IO.Path]::DirectorySeparatorChar
        if (!$resolvedRoot.StartsWith($artifactRoot, [StringComparison]::OrdinalIgnoreCase) -or (Get-Item -LiteralPath $resolvedRoot).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) {
            throw 'Refusing to remove an unexpected update probe directory.'
        }
        $cleanupDeadline = [DateTime]::UtcNow.AddSeconds(5)
        do {
            try {
                Remove-Item -LiteralPath $resolvedRoot -Recurse -Force
                break
            }
            catch [IO.IOException] {
                if ([DateTime]::UtcNow -ge $cleanupDeadline) { throw }
                Start-Sleep -Milliseconds 100
            }
        } while ($true)
    }
}
