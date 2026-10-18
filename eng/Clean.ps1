[CmdletBinding(SupportsShouldProcess)]
param()

$ErrorActionPreference = 'Stop'
$taskRoot = (Get-Item -LiteralPath (Split-Path $PSScriptRoot -Parent)).FullName
$taskPrefix = $taskRoot.TrimEnd('\') + '\'
$taskReportPath = Join-Path $taskRoot 'artifacts/verification.json'
$taskReport = Get-Content -LiteralPath $taskReportPath -Raw | ConvertFrom-Json
$taskExecutable = Get-Item -LiteralPath (Join-Path $taskRoot 'artifacts/single-file/Kadr.exe')
$taskArchive = Get-Item -LiteralPath (Join-Path $taskRoot 'artifacts/Kadr-win-x64.zip')
if (!$taskReport.Passed -or (Get-FileHash -LiteralPath $taskExecutable.FullName).Hash -ne $taskReport.Sha256 -or (Get-FileHash -LiteralPath $taskArchive.FullName).Hash -ne $taskReport.Archive.Sha256) {
    throw 'Keep old builds until the current EXE and archive have passed verification.'
}

$taskTargets = [Collections.Generic.List[System.IO.FileSystemInfo]]::new()
Get-ChildItem -LiteralPath (Join-Path $taskRoot 'artifacts') -Force |
    Where-Object { $_.Name -notin @('single-file', 'Kadr-win-x64.zip', 'verification.json') } |
    ForEach-Object { $taskTargets.Add($_) }
Get-ChildItem -LiteralPath (Join-Path $taskRoot 'docs') -File |
    Where-Object { $_.Name -match '^(editor-\d.*|single-file-\d.*|fix|refinement)-verification\.md$' } |
    ForEach-Object { $taskTargets.Add($_) }
foreach ($taskProjectRoot in @('src', 'tests')) {
    foreach ($taskProject in Get-ChildItem -LiteralPath (Join-Path $taskRoot $taskProjectRoot) -Directory) {
        Get-ChildItem -LiteralPath $taskProject.FullName -Directory -Force |
            Where-Object { $_.Name -in @('bin', 'obj', 'TestResults') } |
            ForEach-Object { $taskTargets.Add($_) }
    }
}
$taskHelper = Join-Path $taskRoot '.tools/MemberOrder'
if (Test-Path -LiteralPath $taskHelper) {
    Get-ChildItem -LiteralPath $taskHelper -Directory -Force |
        Where-Object { $_.Name -in @('bin', 'obj') } |
        ForEach-Object { $taskTargets.Add($_) }
}
$taskInstaller = Join-Path $taskRoot '.tools/dotnet-install.ps1'
if (Test-Path -LiteralPath $taskInstaller) {
    $taskTargets.Add((Get-Item -LiteralPath $taskInstaller))
}

$taskProcesses = @(Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($taskPrefix, [StringComparison]::OrdinalIgnoreCase) })
$taskPlannedBytes = 0L
$taskPlannedFiles = 0L
foreach ($taskItem in $taskTargets) {
    $taskAbsolute = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $taskItem.FullName).Path)
    if (!$taskAbsolute.StartsWith($taskPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Cleanup target outside workspace: $taskAbsolute"
    }
    $taskAncestor = $taskItem
    while ($null -ne $taskAncestor -and $taskAncestor.FullName.StartsWith($taskPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        if (($taskAncestor.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Reparse point in cleanup path: $($taskAncestor.FullName)"
        }
        $taskAncestor = if ($taskAncestor.PSIsContainer) { $taskAncestor.Parent } else { $taskAncestor.Directory }
    }
    if ($taskItem.PSIsContainer -and @(Get-ChildItem -LiteralPath $taskAbsolute -Recurse -Force -Attributes ReparsePoint).Count -gt 0) {
        throw "Reparse point inside cleanup target: $taskAbsolute"
    }
    if (@($taskProcesses | Where-Object { $_.ExecutablePath.Equals($taskAbsolute, [StringComparison]::OrdinalIgnoreCase) -or $_.ExecutablePath.StartsWith($taskAbsolute.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0) {
        throw "Exit the application using this directory before cleanup: $taskAbsolute"
    }
    $taskFiles = if ($taskItem.PSIsContainer) { @(Get-ChildItem -LiteralPath $taskAbsolute -Recurse -File -Force) } else { @($taskItem) }
    $taskPlannedBytes += [long](($taskFiles | Measure-Object Length -Sum).Sum)
    $taskPlannedFiles += $taskFiles.Count
}

$taskRemovedTargets = 0
foreach ($taskItem in $taskTargets) {
    if ($PSCmdlet.ShouldProcess($taskItem.FullName, 'Delete generated files or superseded verification report')) {
        Remove-Item -LiteralPath $taskItem.FullName -Recurse -Force -Confirm:$false
        $taskRemovedTargets++
    }
}
$taskSummary = [pscustomobject]@{
    PlannedFiles = $taskPlannedFiles
    PlannedBytes = $taskPlannedBytes
    RemovedTargets = $taskRemovedTargets
    Completed = !$WhatIfPreference -and $taskRemovedTargets -eq $taskTargets.Count
}
if ($taskSummary.Completed) {
    $taskReport | Add-Member -NotePropertyName Cleanup -NotePropertyValue $taskSummary -Force
    $taskReport | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $taskReportPath -Encoding utf8
}
$taskSummary
