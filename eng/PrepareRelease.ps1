param([switch]$SkipPublish, [string]$PublishDirectory)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$dotnet = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
if (!(Test-Path -LiteralPath $dotnet)) { $dotnet = 'dotnet' }
if ([string]::IsNullOrWhiteSpace($PublishDirectory)) { $PublishDirectory = Join-Path $projectRoot 'artifacts\single-file' }
elseif (![IO.Path]::IsPathRooted($PublishDirectory)) { $PublishDirectory = Join-Path $projectRoot $PublishDirectory }
if (!$SkipPublish) { & (Join-Path $PSScriptRoot 'Publish.ps1') -OutputDirectory $PublishDirectory }
$source = Join-Path $PublishDirectory 'Kadr.exe'
if (!(Test-Path -LiteralPath $source)) { throw 'Publish Kadr before preparing a release.' }
$release = Join-Path $projectRoot 'artifacts\release'
New-Item -ItemType Directory -Path $release -Force | Out-Null
$asset = Join-Path $release 'Kadr-win-x64.exe'
Copy-Item -LiteralPath $source -Destination $asset -Force
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($asset).ProductVersion.Split('+')[0]
& $dotnet run --project (Join-Path $PSScriptRoot 'Kadr.ReleaseTool\Kadr.ReleaseTool.csproj') -c Release -- sign $asset $version
if ($LASTEXITCODE -ne 0) { throw 'Release signing failed.' }
Write-Host "Upload the three files from $release to GitHub release v$version."
