param(
    [Parameter(Mandatory = $true)][string]$ExecutablePath,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ProbeProcess.ps1')
$taskRoot = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $taskRoot 'artifacts/single-file-verification'
}
$taskRun = New-Item -ItemType Directory -Force -Path (Join-Path $OutputDirectory ([guid]::NewGuid().ToString('N')))
$taskIsolated = New-Item -ItemType Directory -Path (Join-Path $taskRun.FullName 'Только EXE')
$taskCache = New-Item -ItemType Directory -Path (Join-Path $taskRun.FullName 'Пустой кэш')
$taskSource = Get-Item -LiteralPath $ExecutablePath
$taskExecutable = Join-Path $taskIsolated.FullName 'Кадр тест.exe'
Copy-Item -LiteralPath $taskSource.FullName -Destination $taskExecutable
$taskEnvironment = @{
    DOTNET_BUNDLE_EXTRACT_BASE_DIR = $taskCache.FullName
    DOTNET_ROOT = (Join-Path $taskRun.FullName 'NoInstalledRuntime')
    DOTNET_ROOT_X64 = (Join-Path $taskRun.FullName 'NoInstalledRuntime')
    PATH = "$env:SystemRoot\System32;$env:SystemRoot"
}

function Get-UserSettingsFingerprint {
    $taskUserSettings = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Kadr/settings.json'
    if (!(Test-Path -LiteralPath $taskUserSettings)) { return 'missing' }
    $taskFile = Get-Item -LiteralPath $taskUserSettings
    return '{0}:{1}:{2}' -f $taskFile.Length, $taskFile.LastWriteTimeUtc.Ticks, (Get-FileHash -LiteralPath $taskUserSettings -Algorithm SHA256).Hash
}

function Invoke-Probe([string]$Name, [string[]]$ProbeArguments) {
    $taskSettingsBefore = Get-UserSettingsFingerprint
    $taskWatch = [System.Diagnostics.Stopwatch]::StartNew()
    $taskProcess = New-ProbeProcess -Executable $taskExecutable -Arguments $ProbeArguments -WorkingDirectory $taskIsolated.FullName -ProcessEnvironment $taskEnvironment
    try {
        if (!$taskProcess.Start()) { throw "Could not start $Name." }
        $taskOutput = $taskProcess.StandardOutput.ReadToEndAsync()
        $taskError = $taskProcess.StandardError.ReadToEndAsync()
        $taskProcess.WaitForExit()
        [IO.File]::WriteAllText((Join-Path $taskRun.FullName "$Name.stdout"), $taskOutput.GetAwaiter().GetResult())
        [IO.File]::WriteAllText((Join-Path $taskRun.FullName "$Name.stderr"), $taskError.GetAwaiter().GetResult())
        if ((Get-UserSettingsFingerprint) -cne $taskSettingsBefore) { throw "User settings changed during $Name. Verification must not modify the user profile." }
        if ($taskProcess.ExitCode -ne 0) { throw "$Name failed with exit code $($taskProcess.ExitCode). See $($taskRun.FullName)." }
        [pscustomobject]@{ Name = $Name; ExitCode = $taskProcess.ExitCode; ElapsedMs = $taskWatch.Elapsed.TotalMilliseconds }
    }
    finally {
        $taskProcess.Dispose()
    }
}

$taskColdPath = Join-Path $taskRun.FullName 'cold.json'
$taskExecutions = @()
$taskExecutions += Invoke-Probe 'cold' @('--distribution-probe', ('"{0}"' -f $taskColdPath))
$taskCold = Get-Content -LiteralPath $taskColdPath -Raw -Encoding UTF8 | ConvertFrom-Json
$taskCacheBefore = @(Get-ChildItem -LiteralPath $taskCache.FullName -Recurse -File | ForEach-Object { "$($_.FullName):$($_.Length):$($_.LastWriteTimeUtc.Ticks)" })
if ($taskCacheBefore.Count -eq 0) { throw 'No native libraries were extracted.' }
$taskWarmPath = Join-Path $taskRun.FullName 'warm.json'
$taskExecutions += Invoke-Probe 'warm' @('--distribution-probe', ('"{0}"' -f $taskWarmPath))
$taskWarm = Get-Content -LiteralPath $taskWarmPath -Raw -Encoding UTF8 | ConvertFrom-Json
$taskCacheAfter = @(Get-ChildItem -LiteralPath $taskCache.FullName -Recurse -File | ForEach-Object { "$($_.FullName):$($_.Length):$($_.LastWriteTimeUtc.Ticks)" })
if (@(Compare-Object $taskCacheBefore $taskCacheAfter).Count -ne 0) { throw 'The warm launch rewrote the native extraction cache.' }
if (!$taskCold.Passed -or !$taskWarm.Passed) { throw 'Offline OCR verification failed.' }
if (!$taskCold.PaddleOcrRecognized -or !$taskWarm.PaddleOcrRecognized) { throw 'Offline PaddleOCR verification failed.' }
$taskForeignModules = @($taskCold.NativeModules | Where-Object { !$_.FileName.StartsWith($taskCache.FullName, [System.StringComparison]::OrdinalIgnoreCase) })
if ($taskForeignModules.Count -ne 0) { throw 'A native OCR dependency was loaded outside the bundle cache.' }

$taskSettingsPath = Join-Path $taskRun.FullName 'settings.json'
$taskCapturePath = Join-Path $taskRun.FullName 'capture.json'
$taskRenderPath = Join-Path $taskRun.FullName 'render.json'
$taskLicenses = Join-Path $taskRun.FullName 'Licenses'
$taskProfileDirectory = New-Item -ItemType Directory -Force -Path ($taskSettingsPath + '.data')
$taskProfileSettings = Join-Path $taskProfileDirectory.FullName 'settings.json'
[IO.File]::WriteAllText($taskProfileSettings, '{"Thickness":23,"Color":4279383126,"AutoStart":false}', [Text.UTF8Encoding]::new($false))
$taskExecutions += Invoke-Probe 'settings' @('--settings-probe', ('"{0}"' -f $taskSettingsPath))
$taskExecutions += Invoke-Probe 'capture' @('--capture-probe', ('"{0}"' -f $taskCapturePath), '--verify-freshness')
$taskExecutions += Invoke-Probe 'render' @('--render-probe', ('"{0}"' -f $taskRenderPath))
$taskExecutions += Invoke-Probe 'licenses' @('--export-licenses', ('"{0}"' -f $taskLicenses))
if (!(Get-Content -LiteralPath $taskSettingsPath -Raw -Encoding UTF8 | ConvertFrom-Json).Passed) { throw 'Native UI verification failed.' }
if (!(Test-Path -LiteralPath $taskProfileSettings)) { throw 'Native settings verification did not write to its isolated profile.' }
$taskSavedProfile = Get-Content -LiteralPath $taskProfileSettings -Raw -Encoding UTF8 | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($taskSavedProfile.LocalizationId) -or $taskSavedProfile.Thickness -ne 23 -or $taskSavedProfile.Color -ne 4279383126 -or $taskSavedProfile.AutoStart -ne $false) { throw 'Native settings verification did not preserve its isolated profile preferences.' }
$taskCaptures = Get-Content -LiteralPath $taskCapturePath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($taskCaptures.Count -ne 4 -or @($taskCaptures | Where-Object { !$_.Passed }).Count -ne 0) { throw 'DXGI freshness verification failed.' }
foreach ($taskLicense in @(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'LICENSES') -File) + @(Get-Item -LiteralPath (Join-Path $taskRoot 'THIRD-PARTY-NOTICES.md')) + @(Get-Item -LiteralPath (Join-Path $taskRoot 'LICENSE'))) {
    $taskExported = Join-Path $taskLicenses $taskLicense.Name
    if ((Get-FileHash -LiteralPath $taskLicense.FullName).Hash -ne (Get-FileHash -LiteralPath $taskExported).Hash) { throw "License mismatch: $($taskLicense.Name)" }
}
if (@(Get-ChildItem -LiteralPath $taskIsolated.FullName -Force).Count -ne 1) { throw 'Files appeared beside the isolated EXE.' }
$taskResult = [pscustomobject]@{
    Passed = $true
    Executable = $taskSource.FullName
    Sha256 = (Get-FileHash -LiteralPath $taskSource.FullName).Hash
    Bytes = $taskSource.Length
    NativeCacheReused = $true
    OfflineOcr = $true
    OfflinePaddleOcr = $true
    NativeDependenciesFromBundle = $true
    IsolatedDirectoryContainsOnlyExe = $true
    UserSettingsUnchanged = $true
    RunDirectory = $taskRun.FullName
    Probes = $taskExecutions
}
$taskReport = Join-Path $OutputDirectory 'verification.json'
$taskResult | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $taskReport -Encoding utf8
$taskResult | ConvertTo-Json -Depth 5
