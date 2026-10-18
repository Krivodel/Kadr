param(
    [string]$OutputDirectory,
    [string]$CrtDirectory
)

$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskLocalSdk = Join-Path $taskRoot '.tools/dotnet/dotnet.exe'
$taskDotnet = if (Test-Path -LiteralPath $taskLocalSdk) { $taskLocalSdk } else { 'dotnet' }

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $taskRoot 'artifacts/single-file'
}

if ((Test-Path -LiteralPath $OutputDirectory) -and @(Get-ChildItem -LiteralPath $OutputDirectory -Force | Where-Object { $_.Name -ne 'Kadr.exe' }).Count -gt 0) {
    throw 'Use an empty output directory or one containing only the previous Kadr.exe.'
}

if ([string]::IsNullOrWhiteSpace($CrtDirectory)) {
    $taskVsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    if (Test-Path -LiteralPath $taskVsWhere) {
        $taskVisualStudio = & $taskVsWhere -latest -property installationPath
        $taskRedistRoot = Join-Path $taskVisualStudio 'VC/Redist/MSVC'
        $CrtDirectory = Get-ChildItem -LiteralPath $taskRedistRoot -Directory |
            Where-Object { $_.Name -match '^\d+\.' } |
            Sort-Object Name -Descending |
            ForEach-Object { Join-Path $_.FullName 'x64/Microsoft.VC143.CRT' } |
            Where-Object { Test-Path -LiteralPath $_ } |
            Select-Object -First 1
    }
}

if ([string]::IsNullOrWhiteSpace($CrtDirectory) -or !(Test-Path -LiteralPath $CrtDirectory)) {
    throw 'Pass -CrtDirectory with the Visual C++ x64 app-local redistributable directory. OCR requires this runtime.'
}

& $taskDotnet publish (Join-Path $taskRoot 'src/Kadr.Desktop/Kadr.Desktop.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true "-p:CrtDirectory=$CrtDirectory" -o $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw 'Kadr publish failed.' }

$taskPublishedFiles = @(Get-ChildItem -LiteralPath $OutputDirectory -Recurse -File)
if ($taskPublishedFiles.Count -ne 1 -or $taskPublishedFiles[0].Name -ne 'Kadr.exe') {
    throw 'Single-file publication must contain only Kadr.exe. Use a new, empty output directory.'
}
Write-Output (Join-Path $OutputDirectory 'Kadr.exe')
