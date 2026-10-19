function Get-CleanupProcessPaths {
    # CIM module initialization creates aliases; keep those out of -WhatIf output.
    & {
        $WhatIfPreference = $false
        Get-CimInstance Win32_Process -ErrorAction Stop |
            Where-Object { $_.ExecutablePath } | ForEach-Object { $_.ExecutablePath }
    }
}

function Get-CleanupTargetInfo {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$AllowedRoot,
        [AllowEmptyCollection()][string[]]$ProcessPaths = @()
    )

    $absolute = [IO.Path]::GetFullPath($Path)
    $prefix = [IO.Path]::GetFullPath($AllowedRoot).TrimEnd('\') + '\'
    if (!$absolute.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Cleanup target outside allowed directory: $absolute"
    }

    # Check ancestors before opening the target, including missing targets below junctions.
    $ancestorPath = $absolute
    while ($ancestorPath) {
        if (Test-Path -LiteralPath $ancestorPath) {
            $ancestor = Get-Item -LiteralPath $ancestorPath -Force -ErrorAction Stop
            if (($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Reparse point in cleanup path: $ancestorPath"
            }
        }
        $ancestorPath = [IO.Path]::GetDirectoryName($ancestorPath)
    }
    if (!(Test-Path -LiteralPath $absolute)) {
        return
    }

    foreach ($processPath in $ProcessPaths) {
        if ($processPath.Equals($absolute, [StringComparison]::OrdinalIgnoreCase) -or
            $processPath.StartsWith($absolute.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw "Exit the application using this directory before cleanup: $absolute"
        }
    }

    $pending = [Collections.Generic.Stack[IO.FileSystemInfo]]::new()
    $pending.Push((Get-Item -LiteralPath $absolute -Force -ErrorAction Stop))
    $bytes = 0L
    $files = 0L
    while ($pending.Count -gt 0) {
        $item = $pending.Pop()
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Reparse point inside cleanup target: $($item.FullName)"
        }
        if ($item.PSIsContainer) {
            foreach ($child in Get-ChildItem -LiteralPath $item.FullName -Force -ErrorAction Stop) {
                $pending.Push($child)
            }
        }
        else {
            $bytes += $item.Length
            $files++
        }
    }

    [pscustomobject]@{
        Path = $absolute
        Root = $AllowedRoot
        Files = $files
        Bytes = $bytes
    }
}
