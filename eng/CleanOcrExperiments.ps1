#requires -Version 5.1
<#
.SYNOPSIS
Deletes obsolete OCR experiments and their models. Keeps settings and current PaddleOCR.
.EXAMPLE
./eng/CleanOcrExperiments.ps1 -WhatIf
.EXAMPLE
./eng/CleanOcrExperiments.ps1
#>
[CmdletBinding(SupportsShouldProcess)]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'CleanupPaths.ps1')

$projectRoot = Split-Path $PSScriptRoot -Parent
$relativePaths = @(
    'artifacts/chandra-build'
    'artifacts/chandra-probe'
    'artifacts/medium-build'
    'artifacts/medium-diagnostics'
    'artifacts/medium-probe'
    'artifacts/medium-runtime'
    'artifacts/medium-source'
    'artifacts/medium-verification'
    'artifacts/paddle-build'
    'artifacts/paddle-server-verification'
    'artifacts/paddle-server-build'
    'assets/ocr/eng.traineddata'
    'assets/ocr/rus.traineddata'
    'assets/ocr/paddle-medium-ru'
    'assets/ocr/paddle-server'
)
$candidates = @($relativePaths | ForEach-Object {
    [pscustomobject]@{ Path = Join-Path $projectRoot $_; Root = $projectRoot }
})
$localData = [Environment]::GetFolderPath('LocalApplicationData')
if ([string]::IsNullOrWhiteSpace($localData)) {
    throw 'LocalApplicationData is unavailable. Cleanup stopped.'
}
$modelRoot = Join-Path $localData 'Kadr/tessdata'
$candidates += [pscustomobject]@{ Path = Join-Path $modelRoot 'chandra-2'; Root = $modelRoot }

$processPaths = @(Get-CleanupProcessPaths)
$plan = @($candidates | ForEach-Object {
    Get-CleanupTargetInfo -Path $_.Path -AllowedRoot $_.Root -ProcessPaths $processPaths
})
$plannedBytes = 0L
foreach ($target in $plan) {
    $plannedBytes += $target.Bytes
}
Write-Host ('Found {0} obsolete targets, {1:N2} GB ({2:N2} GiB).' -f $plan.Count, ($plannedBytes / 1e9), ($plannedBytes / 1GB))

$removedTargets = 0
$removedBytes = 0L
foreach ($target in $plan) {
    if ($PSCmdlet.ShouldProcess($target.Path, 'Delete obsolete OCR experiment')) {
        # Recheck immediately before deletion in case a build was started after preflight.
        $processPaths = @(Get-CleanupProcessPaths)
        $current = Get-CleanupTargetInfo -Path $target.Path -AllowedRoot $target.Root -ProcessPaths $processPaths
        if ($null -ne $current) {
            Remove-Item -LiteralPath $current.Path -Recurse -Force -Confirm:$false -ErrorAction Stop
            $removedTargets++
            $removedBytes += $current.Bytes
        }
    }
}

[pscustomobject]@{
    PlannedTargets = $plan.Count
    PlannedBytes = $plannedBytes
    RemovedTargets = $removedTargets
    RemovedBytes = $removedBytes
    Preview = [bool]$WhatIfPreference
}
