param(
    [Parameter(Mandatory = $true)][string]$ExecutablePath,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
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

function Invoke-Probe([string]$Name, [string[]]$ProbeArguments) {
    $taskWatch = [System.Diagnostics.Stopwatch]::StartNew()
    $taskProcess = Start-Process -FilePath $taskExecutable -ArgumentList $ProbeArguments -Environment $taskEnvironment -WorkingDirectory $taskIsolated.FullName -WindowStyle Hidden -PassThru -Wait -RedirectStandardOutput (Join-Path $taskRun.FullName "$Name.stdout") -RedirectStandardError (Join-Path $taskRun.FullName "$Name.stderr")
    if ($taskProcess.ExitCode -ne 0) { throw "$Name failed with exit code $($taskProcess.ExitCode). See $($taskRun.FullName)." }
    [pscustomobject]@{ Name = $Name; ExitCode = $taskProcess.ExitCode; ElapsedMs = $taskWatch.Elapsed.TotalMilliseconds }
}

$taskColdPath = Join-Path $taskRun.FullName 'cold.json'
$taskExecutions = @()
$taskExecutions += Invoke-Probe 'cold' @('--distribution-probe', ('"{0}"' -f $taskColdPath))
$taskCold = Get-Content -LiteralPath $taskColdPath -Raw | ConvertFrom-Json
$taskCacheBefore = @(Get-ChildItem -LiteralPath $taskCache.FullName -Recurse -File | ForEach-Object { "$($_.FullName):$($_.Length):$($_.LastWriteTimeUtc.Ticks)" })
if ($taskCacheBefore.Count -eq 0) { throw 'No native libraries were extracted.' }
$taskWarmPath = Join-Path $taskRun.FullName 'warm.json'
$taskExecutions += Invoke-Probe 'warm' @('--distribution-probe', ('"{0}"' -f $taskWarmPath))
$taskWarm = Get-Content -LiteralPath $taskWarmPath -Raw | ConvertFrom-Json
$taskCacheAfter = @(Get-ChildItem -LiteralPath $taskCache.FullName -Recurse -File | ForEach-Object { "$($_.FullName):$($_.Length):$($_.LastWriteTimeUtc.Ticks)" })
if (@(Compare-Object $taskCacheBefore $taskCacheAfter).Count -ne 0) { throw 'The warm launch rewrote the native extraction cache.' }
if (!$taskCold.Passed -or !$taskWarm.Passed) { throw 'Offline OCR verification failed.' }
$taskForeignModules = @($taskCold.NativeModules | Where-Object { !$_.FileName.StartsWith($taskCache.FullName, [System.StringComparison]::OrdinalIgnoreCase) })
if ($taskForeignModules.Count -ne 0) { throw 'A native OCR dependency was loaded outside the bundle cache.' }

$taskSettingsPath = Join-Path $taskRun.FullName 'settings.json'
$taskCapturePath = Join-Path $taskRun.FullName 'capture.json'
$taskRenderPath = Join-Path $taskRun.FullName 'render.json'
$taskLicenses = Join-Path $taskRun.FullName 'Licenses'
$taskExecutions += Invoke-Probe 'settings' @('--settings-probe', ('"{0}"' -f $taskSettingsPath))
$taskExecutions += Invoke-Probe 'capture' @('--capture-probe', ('"{0}"' -f $taskCapturePath), '--verify-freshness')
$taskExecutions += Invoke-Probe 'render' @('--render-probe', ('"{0}"' -f $taskRenderPath))
$taskExecutions += Invoke-Probe 'licenses' @('--export-licenses', ('"{0}"' -f $taskLicenses))
if (!(Get-Content -LiteralPath $taskSettingsPath -Raw | ConvertFrom-Json).Passed) { throw 'Native UI verification failed.' }
$taskCaptures = @(Get-Content -LiteralPath $taskCapturePath -Raw | ConvertFrom-Json)
if ($taskCaptures.Count -ne 4 -or @($taskCaptures | Where-Object { !$_.Passed }).Count -ne 0) { throw 'DXGI freshness verification failed.' }
foreach ($taskLicense in @(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'LICENSES') -File) + @(Get-Item -LiteralPath (Join-Path $taskRoot 'THIRD-PARTY-NOTICES.md'))) {
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
    NativeDependenciesFromBundle = $true
    IsolatedDirectoryContainsOnlyExe = $true
    RunDirectory = $taskRun.FullName
    Probes = $taskExecutions
}
$taskReport = Join-Path $OutputDirectory 'verification.json'
$taskResult | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $taskReport -Encoding utf8
$taskResult | ConvertTo-Json -Depth 5
