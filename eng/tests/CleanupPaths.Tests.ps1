#requires -Version 5.1
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$engRoot = Split-Path $PSScriptRoot -Parent
$projectRoot = Split-Path $engRoot -Parent
$helperPath = Join-Path $engRoot 'CleanupPaths.ps1'
. $helperPath

function Assert-CleanupRejected {
    param([scriptblock]$Action, [string]$Message)
    try {
        & $Action | Out-Null
    }
    catch {
        if ($_.Exception.Message -notlike $Message) {
            throw
        }
        return
    }
    throw "Expected cleanup rejection: $Message"
}

$info = Get-CleanupTargetInfo -Path $helperPath -AllowedRoot $projectRoot
if ($info.Files -ne 1 -or $info.Bytes -ne (Get-Item -LiteralPath $helperPath).Length) {
    throw 'File count or byte count is incorrect.'
}
Assert-CleanupRejected { Get-CleanupTargetInfo -Path $projectRoot -AllowedRoot $projectRoot } '*outside allowed directory*'
Assert-CleanupRejected { Get-CleanupTargetInfo -Path (Join-Path $projectRoot '../Pica') -AllowedRoot $projectRoot } '*outside allowed directory*'
Assert-CleanupRejected { Get-CleanupTargetInfo -Path ($projectRoot + '-other/file') -AllowedRoot $projectRoot } '*outside allowed directory*'
Assert-CleanupRejected { Get-CleanupTargetInfo -Path $helperPath -AllowedRoot $projectRoot -ProcessPaths @($helperPath) } '*Exit the application*'
Assert-CleanupRejected { Get-CleanupTargetInfo -Path $engRoot -AllowedRoot $projectRoot -ProcessPaths @($helperPath) } '*Exit the application*'
$info = Get-CleanupTargetInfo -Path $helperPath -AllowedRoot $projectRoot -ProcessPaths @($helperPath + '-other')
if ($null -eq $info) {
    throw 'Unrelated process should not block cleanup.'
}

& {
    function Get-Item {
        [CmdletBinding()]
        param([string]$LiteralPath, [switch]$Force)
        [pscustomobject]@{ Attributes = [IO.FileAttributes]::ReparsePoint }
    }
    Assert-CleanupRejected { Get-CleanupTargetInfo -Path $helperPath -AllowedRoot $projectRoot } '*Reparse point*'
}

$cleanScript = Join-Path $engRoot 'CleanOcrExperiments.ps1'
& {
    function Remove-Item { throw 'Deletion must never be called during preview.' }
    $summary = & $cleanScript -WhatIf
    if (!$summary.Preview -or $summary.RemovedTargets -ne 0 -or $summary.RemovedBytes -ne 0) {
        throw 'Preview attempted to delete files.'
    }
}
& {
    function Remove-Item { throw 'Deletion must never be called for missing targets.' }
    function Test-Path { return $false }
    $summary = & $cleanScript -WhatIf
    if ($summary.PlannedTargets -ne 0 -or $summary.PlannedBytes -ne 0) {
        throw 'Missing targets should be skipped.'
    }
}
Write-Host 'Cleanup checks passed; deletion was not executed.'
