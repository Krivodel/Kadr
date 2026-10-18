#requires -Version 7.2
[CmdletBinding(SupportsShouldProcess)]
param(
    [switch]$Login,
    [string]$NotesFile,
    [string]$CrtDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = Split-Path $PSScriptRoot -Parent
$repository = 'Krivodel/Kadr'
$lock = $null

function Invoke-ReleaseCommand([string]$Command, [string[]]$CommandArguments) {
    $savedTokens = @{}
    if ($Command -in @('gh', 'git')) {
        foreach ($name in @('GH_TOKEN', 'GITHUB_TOKEN')) {
            $savedTokens[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        }
    }
    try {
        foreach ($name in $savedTokens.Keys) { [Environment]::SetEnvironmentVariable($name, [NullString]::Value, 'Process') }
        & $Command @CommandArguments
        if ($LASTEXITCODE -ne 0) { throw "$Command failed (exit code $LASTEXITCODE). Release publication stopped." }
    }
    finally {
        foreach ($name in $savedTokens.Keys) {
            $value = $savedTokens[$name]
            if ($null -eq $value) { $value = [NullString]::Value }
            [Environment]::SetEnvironmentVariable($name, $value, 'Process')
        }
    }
}

function Assert-GitHubCli {
    if (!(Get-Command gh -ErrorAction SilentlyContinue)) {
        throw 'Install the Windows amd64 MSI from https://github.com/cli/cli/releases/latest, reopen the terminal, then run ./eng/Release.ps1 -Login.'
    }
}

function Invoke-ReleaseApi([string]$Endpoint, [string[]]$ApiArguments = @()) {
    (Invoke-ReleaseCommand gh (@('api', '--hostname', 'github.com', $Endpoint) + $ApiArguments)) | ConvertFrom-Json
}

function Assert-ReleaseSource([string]$Commit) {
    $currentCommit = Invoke-ReleaseCommand git @('rev-parse', 'HEAD')
    $changes = Invoke-ReleaseCommand git @('status', '--porcelain', '--untracked-files=no')
    $untracked = Invoke-ReleaseCommand git @('ls-files', '--others', '--exclude-standard', '--', 'src', 'tests', 'eng', 'assets', 'LICENSES', ':(top,glob)*.props', ':(top,glob)*.targets', ':(top,glob)*.json', ':(top,glob)*.slnx')
    if ($currentCommit -ne $Commit -or $changes -or $untracked) {
        throw 'Commit all source changes before releasing. The release must match HEAD.'
    }
}

function Get-ReleaseTagCommit([string]$Tag) {
    $references = @(Invoke-ReleaseCommand git @('ls-remote', '--tags', 'origin', "refs/tags/$Tag", "refs/tags/$Tag^{}"))
    if ($references.Count -gt 0) { return ($references[-1] -split '\s+')[0] }
    return $null
}

if ($NotesFile) { $NotesFile = (Resolve-Path -LiteralPath $NotesFile).ProviderPath }
if ($CrtDirectory) { $CrtDirectory = (Resolve-Path -LiteralPath $CrtDirectory).ProviderPath }
Push-Location $projectRoot
try {
    if ($Login) {
        if ($PSCmdlet.ShouldProcess('github.com', 'Sign in to GitHub CLI through the browser')) {
            Assert-GitHubCli
            Invoke-ReleaseCommand gh @('auth', 'login', '--hostname', 'github.com', '--git-protocol', 'https', '--web') | Out-Host
        }
        return
    }
    [xml]$project = Get-Content -LiteralPath 'src/Kadr.Desktop/Kadr.Desktop.csproj' -Raw
    $version = [string]$project.Project.PropertyGroup.Version
    if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Set a stable X.Y.Z Version in Kadr.Desktop.csproj.' }
    $tag = "v$version"
    $commit = Invoke-ReleaseCommand git @('rev-parse', 'HEAD')
    $branch = Invoke-ReleaseCommand git @('symbolic-ref', '--short', 'HEAD')
    if (!$PSCmdlet.ShouldProcess("$repository $tag ($commit)", "Test, build, sign, verify, push $branch and release tag, then publish GitHub Release")) { return }

    Assert-ReleaseSource $commit
    foreach ($direction in @('fetch', 'push')) {
        $remoteArguments = @('remote', 'get-url', '--all')
        if ($direction -eq 'push') { $remoteArguments += '--push' }
        $urls = @(Invoke-ReleaseCommand git ($remoteArguments + 'origin'))
        if ($urls.Count -ne 1 -or $urls[0] -notmatch '^(https://github\.com/|git@github\.com:|ssh://git@github\.com/)Krivodel/Kadr(\.git)?/?$') {
            throw 'origin must point only to github.com/Krivodel/Kadr for both fetch and push.'
        }
    }
    Assert-GitHubCli
    Invoke-ReleaseCommand gh @('auth', 'status', '--hostname', 'github.com') | Out-Host
    $metadata = Invoke-ReleaseApi "repos/$repository"
    if ($metadata.private) { throw 'Automatic updates require a public GitHub repository.' }
    $pages = Invoke-ReleaseApi "repos/$repository/releases" @('--paginate', '--slurp')
    $releases = @($pages | ForEach-Object { foreach ($release in $_) { $release } })
    $existing = @($releases | Where-Object { $_.tag_name -eq $tag })
    if ($existing.Count -gt 0 -and !$existing[0].draft) { throw "$tag is already published. Increase Version; published releases are never overwritten." }
    foreach ($release in $releases) {
        if (!$release.draft -and !$release.prerelease -and $release.tag_name -match '^v?(\d+\.\d+\.\d+)$' -and [version]$Matches[1] -ge [version]$version) {
            throw "$tag must be newer than published stable releases."
        }
    }
    $tagCommit = Get-ReleaseTagCommit $tag
    if (($tagCommit -and $tagCommit -ne $commit) -or ($existing.Count -gt 0 -and !$tagCommit)) {
        throw "$tag does not point to HEAD. Use the original commit or a new version."
    }

    $artifacts = Join-Path $projectRoot 'artifacts'
    New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
    $lock = [IO.File]::Open((Join-Path $artifacts 'release.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    $dotnet = Join-Path $projectRoot '.tools/dotnet/dotnet.exe'
    if (!(Test-Path -LiteralPath $dotnet)) { $dotnet = 'dotnet' }
    Invoke-ReleaseCommand $dotnet @('test', 'Kadr.slnx', '-c', 'Release') | Out-Host
    & (Join-Path $PSScriptRoot 'PrepareRelease.ps1') -PublishDirectory (Join-Path $artifacts 'release-build') -CrtDirectory $CrtDirectory

    $asset = Join-Path $artifacts 'release/Kadr-win-x64.exe'
    $files = @($asset, "$asset.sha256", "$asset.sig")
    $manifest = Get-Content -LiteralPath "$asset.sig" -Raw | ConvertFrom-Json
    if ($manifest.Version -ne $version -or $manifest.Sha256 -ne (Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash) {
        throw 'The signed executable does not match the release version or hash.'
    }
    & (Join-Path $PSScriptRoot 'VerifySingleFile.ps1') -ExecutablePath $asset -OutputDirectory (Join-Path $artifacts 'release-verification') | Out-Host
    Assert-ReleaseSource $commit

    Invoke-ReleaseCommand git @('-c', 'push.followTags=false', 'push', 'origin', "${commit}:refs/heads/$branch") | Out-Host
    if (!$tagCommit) {
        Invoke-ReleaseCommand git @('-c', 'push.followTags=false', 'push', 'origin', "${commit}:refs/tags/$tag") | Out-Host
    }
    if ((Get-ReleaseTagCommit $tag) -ne $commit) { throw 'The remote release tag does not match the verified build.' }
    if ($existing.Count -eq 0) {
        $createArguments = @('release', 'create', $tag, '--repo', "github.com/$repository", '--verify-tag', '--target', $commit, '--title', "Kadr $version", '--draft')
        if ($NotesFile) { $createArguments += @('--notes-file', $NotesFile) }
        else { $createArguments += '--generate-notes' }
        Invoke-ReleaseCommand gh $createArguments | Out-Host
    }
    $releaseId = Invoke-ReleaseCommand gh @('release', 'view', $tag, '--repo', "github.com/$repository", '--json', 'databaseId', '--jq', '.databaseId')
    if ($releaseId -notmatch '^[1-9]\d*$') { throw 'Could not resolve the draft release ID.' }
    $releaseEndpoint = "repos/$repository/releases/$releaseId"
    $remoteRelease = Invoke-ReleaseApi $releaseEndpoint
    if ($remoteRelease.tag_name -cne $tag -or !$remoteRelease.draft -or $remoteRelease.prerelease) { throw 'Only a matching stable draft can receive release assets.' }
    Invoke-ReleaseCommand gh (@('release', 'upload', $tag, '--repo', "github.com/$repository", '--clobber') + $files) | Out-Host
    $remoteRelease = Invoke-ReleaseApi $releaseEndpoint
    if (!$remoteRelease.draft -or @($remoteRelease.assets).Count -ne $files.Count) { throw 'The draft must contain exactly the three release files.' }
    foreach ($file in $files) {
        $uploaded = @($remoteRelease.assets | Where-Object { $_.name -ceq [IO.Path]::GetFileName($file) })
        $digest = 'sha256:' + (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($uploaded.Count -ne 1 -or $uploaded[0].state -ne 'uploaded' -or $uploaded[0].digest -cne $digest -or $uploaded[0].size -ne (Get-Item -LiteralPath $file).Length) {
            throw 'Uploaded assets do not match the local files. The release remains a draft; rerun to retry.'
        }
    }
    if ($existing.Count -gt 0 -and $NotesFile) {
        Invoke-ReleaseCommand gh @('release', 'edit', $tag, '--repo', "github.com/$repository", '--notes-file', $NotesFile) | Out-Host
    }
    Invoke-ReleaseCommand gh @('release', 'edit', $tag, '--repo', "github.com/$repository", '--draft=false', '--latest', '--verify-tag') | Out-Host
    Write-Host "Published https://github.com/$repository/releases/tag/$tag"
}
finally {
    if ($lock) { $lock.Dispose() }
    Pop-Location
}
