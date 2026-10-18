#requires -Version 7.2
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = Split-Path $PSScriptRoot -Parent
$realGit = (Get-Command git -CommandType Application | Select-Object -First 1).Source
$runRoot = Join-Path $projectRoot ('artifacts/release-tests-' + [guid]::NewGuid().ToString('N'))
$results = [Collections.Generic.List[string]]::new()

function Assert-ReleaseTest([bool]$Condition, [string]$Message) {
    if (!$Condition) { throw $Message }
}

function Assert-IsolatedGitHubEnvironment {
    foreach ($name in @('GH_TOKEN', 'GITHUB_TOKEN')) {
        Assert-ReleaseTest ($null -eq [Environment]::GetEnvironmentVariable($name, 'Process')) "An inherited token reached GitHub CLI or Git: $name."
    }
}

function git {
    Assert-IsolatedGitHubEnvironment
    if ($args[0] -eq 'remote' -and $args[1] -eq 'get-url') {
        $global:LASTEXITCODE = 0
        if ($fixture.ExtraPushTarget -and $args -contains '--push') { return @($fixture.Origin, 'https://github.com/another/repository.git') }
        return $fixture.Origin
    }
    & $realGit @args
}

function dotnet {
    $fixture.TestsRun++
    $global:LASTEXITCODE = if ($fixture.TestFailure) { 1 } else { 0 }
}

function gh {
    Assert-IsolatedGitHubEnvironment
    $global:LASTEXITCODE = 0
    $arguments = @($args)
    $fixture.GitHubCalls++
    if ($arguments[0] -eq 'auth') {
        if ($arguments[1] -eq 'login') {
            $fixture.Logins++
            Assert-ReleaseTest ($arguments -contains '--web') 'Login did not request browser authentication.'
            if ($fixture.LoginFailure) { $global:LASTEXITCODE = 1 }
        }
        return
    }
    if ($arguments[0] -eq 'api') {
        $endpoint = $arguments[3]
        if ($endpoint -eq 'repos/Krivodel/Kadr') { return '{"private":false}' }
        if ($endpoint -eq 'repos/Krivodel/Kadr/releases') {
            return ConvertTo-Json -InputObject (, @($fixture.Releases)) -Depth 8 -Compress
        }
        if ($endpoint -eq 'repos/Krivodel/Kadr/releases/42') {
            return ConvertTo-Json -InputObject $fixture.Draft -Depth 8 -Compress
        }
        throw "Unexpected API endpoint: $endpoint"
    }
    switch ($arguments[1]) {
        'view' { return '42' }
        'create' {
            $fixture.Creates++
            $fixture.Draft = @{ tag_name = 'v1.2.3'; draft = $true; prerelease = $false; assets = @() }
            $fixture.Releases = @($fixture.Draft)
            $targetIndex = [Array]::IndexOf($arguments, '--target')
            Assert-ReleaseTest ($arguments[$targetIndex + 1] -eq $fixture.Commit) 'Release target is not the built commit.'
        }
        'upload' {
            Assert-ReleaseTest $fixture.Draft.draft 'Assets were uploaded to a published release.'
            if ($fixture.UploadFailure) { $global:LASTEXITCODE = 1; return }
            $fixture.Draft.assets = @($arguments | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | ForEach-Object {
                @{
                    name = [IO.Path]::GetFileName($_)
                    size = (Get-Item -LiteralPath $_).Length
                    state = 'uploaded'
                    digest = if ($fixture.CorruptUpload) { 'sha256:wrong' } else { 'sha256:' + (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() }
                }
            })
        }
        'edit' {
            if ($arguments -contains '--draft=false') {
                Assert-ReleaseTest (@($fixture.Draft.assets).Count -eq 3) 'Release was published without all assets.'
                $fixture.Published = $true
                $fixture.Draft.draft = $false
            }
        }
        default { throw "Unexpected GitHub command: $arguments" }
    }
}

function New-ReleaseFixture([string]$Name) {
    $directory = Join-Path $runRoot $Name
    $remote = Join-Path $runRoot "$Name.git"
    New-Item -ItemType Directory -Path (Join-Path $directory 'eng'), (Join-Path $directory 'src/Kadr.Desktop') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Release.ps1') -Destination (Join-Path $directory 'eng/Release.ps1')
    Set-Content -LiteralPath (Join-Path $directory '.gitignore') -Value '/artifacts/'
    Set-Content -LiteralPath (Join-Path $directory 'src/Kadr.Desktop/Kadr.Desktop.csproj') -Value '<Project><PropertyGroup><Version>1.2.3</Version></PropertyGroup></Project>'
    Set-Content -LiteralPath (Join-Path $directory 'eng/PrepareRelease.ps1') -Value @'
param([string]$PublishDirectory, [string]$CrtDirectory)
$fixture.Builds++
$assets = Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/release'
New-Item -ItemType Directory -Path $assets -Force | Out-Null
$exe = Join-Path $assets 'Kadr-win-x64.exe'
Set-Content -LiteralPath $exe -Value 'Fixture executable'
$hash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
@{ Version = $fixture.BuildVersion; Sha256 = $hash } | ConvertTo-Json | Set-Content -LiteralPath "$exe.sig"
Set-Content -LiteralPath "$exe.sha256" -Value "$hash  Kadr-win-x64.exe"
if ($fixture.MutateSource) { Add-Content -LiteralPath 'src/Kadr.Desktop/Kadr.Desktop.csproj' -Value ' ' }
'@
    Set-Content -LiteralPath (Join-Path $directory 'eng/VerifySingleFile.ps1') -Value @'
param([string]$ExecutablePath, [string]$OutputDirectory)
if ($fixture.VerificationFailure) { throw 'Fixture verification failed.' }
'@
    & $realGit init --bare --quiet $remote
    & $realGit -C $directory init --quiet --initial-branch=main
    & $realGit -C $directory config user.name ReleaseTest
    & $realGit -C $directory config user.email release@example.invalid
    & $realGit -C $directory config commit.gpgsign false
    & $realGit -C $directory config tag.gpgsign false
    & $realGit -C $directory config core.autocrlf false
    & $realGit -C $directory add .
    & $realGit -C $directory commit --quiet -m 'Release fixture'
    & $realGit -C $directory remote add origin $remote
    $commit = & $realGit -C $directory rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the local Git fixture.' }
    return @{
        Directory = $directory; Remote = $remote; Commit = $commit; Origin = 'https://github.com/Krivodel/Kadr.git'
        Releases = @(); Draft = $null; Published = $false; Creates = 0; Builds = 0; TestsRun = 0; GitHubCalls = 0
        TestFailure = $false; UploadFailure = $false; CorruptUpload = $false; VerificationFailure = $false; MutateSource = $false
        BuildVersion = '1.2.3'
        ExtraPushTarget = $false
        Logins = 0; LoginFailure = $false; TokensAbsent = $false
    }
}

function Invoke-ReleaseFixture([string]$ExpectedError = '', [switch]$Login, [switch]$Preview) {
    $originalTokens = @{}
    foreach ($name in @('GH_TOKEN', 'GITHUB_TOKEN')) {
        $originalTokens[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
    }
    $expectedToken = if ($fixture.TokensAbsent) { $null } else { 'fixture-token-for-other-projects' }
    try {
        $fixtureToken = $expectedToken
        if ($null -eq $fixtureToken) { $fixtureToken = [NullString]::Value }
        foreach ($name in $originalTokens.Keys) { [Environment]::SetEnvironmentVariable($name, $fixtureToken, 'Process') }
        $failure = $null
        try { & (Join-Path $fixture.Directory 'eng/Release.ps1') -Login:$Login -WhatIf:$Preview }
        catch { $failure = $_.Exception.Message }

        foreach ($name in $originalTokens.Keys) {
            Assert-ReleaseTest ([Environment]::GetEnvironmentVariable($name, 'Process') -ceq $expectedToken) "The caller's environment was not restored: $name."
        }
        if ($ExpectedError) {
            Assert-ReleaseTest ($null -ne $failure -and $failure -like "*$ExpectedError*") "Expected '$ExpectedError', got '$failure'."
            Assert-ReleaseTest (!$fixture.Published) 'A failed release was published.'
        }
        elseif ($failure) { throw $failure }
    }
    finally {
        foreach ($name in $originalTokens.Keys) {
            $value = $originalTokens[$name]
            if ($null -eq $value) { $value = [NullString]::Value }
            [Environment]::SetEnvironmentVariable($name, $value, 'Process')
        }
    }
}

foreach ($scenario in @('Login', 'LoginFailure', 'LoginWithoutTokens', 'LoginWhatIf', 'Success', 'DirtySource', 'UntrackedSource', 'WrongRepository', 'MultiplePushTargets', 'TestFailure', 'WrongBuildVersion', 'VerificationFailure', 'ChangedDuringBuild', 'ExistingRelease', 'OlderVersion', 'WrongTag', 'AnnotatedTag', 'CorruptUpload', 'RetryUpload', 'WhatIf')) {
    $fixture = New-ReleaseFixture $scenario
    switch ($scenario) {
        'LoginFailure' { $fixture.LoginFailure = $true }
        'LoginWithoutTokens' { $fixture.TokensAbsent = $true }
        'Success' {
            & $realGit -C $fixture.Directory config push.followTags true
            & $realGit -C $fixture.Directory tag -a not-for-release -m 'Unrelated local tag'
        }
        'DirtySource' { Add-Content -LiteralPath (Join-Path $fixture.Directory 'src/Kadr.Desktop/Kadr.Desktop.csproj') -Value ' ' }
        'UntrackedSource' { Set-Content -LiteralPath (Join-Path $fixture.Directory 'src/Uncommitted.cs') -Value 'class Uncommitted {}' }
        'WrongRepository' { $fixture.Origin = 'https://github.com/another/repository.git' }
        'MultiplePushTargets' { $fixture.ExtraPushTarget = $true }
        'TestFailure' { $fixture.TestFailure = $true }
        'WrongBuildVersion' { $fixture.BuildVersion = '1.2.2' }
        'VerificationFailure' { $fixture.VerificationFailure = $true }
        'ChangedDuringBuild' { $fixture.MutateSource = $true }
        'ExistingRelease' { $fixture.Releases = @(@{ tag_name = 'v1.2.3'; draft = $false; prerelease = $false }) }
        'OlderVersion' { $fixture.Releases = @(@{ tag_name = 'v1.3.0'; draft = $false; prerelease = $false }) }
        'WrongTag' {
            & $realGit -C $fixture.Directory commit --quiet --allow-empty -m 'Another commit'
            $otherCommit = & $realGit -C $fixture.Directory rev-parse HEAD
            & $realGit --git-dir $fixture.Remote fetch --quiet $fixture.Directory $otherCommit
            & $realGit --git-dir $fixture.Remote update-ref refs/tags/v1.2.3 $otherCommit
            & $realGit -C $fixture.Directory checkout --quiet $fixture.Commit
            & $realGit -C $fixture.Directory switch --quiet -c release-fixture
        }
        'AnnotatedTag' {
            & $realGit -C $fixture.Directory tag -a v1.2.3 -m 'Kadr 1.2.3'
            & $realGit -C $fixture.Directory push --quiet origin refs/tags/v1.2.3
        }
        'CorruptUpload' { $fixture.CorruptUpload = $true }
        'RetryUpload' { $fixture.UploadFailure = $true }
    }

    switch ($scenario) {
        { $_ -in 'Login', 'LoginWithoutTokens' } {
            Invoke-ReleaseFixture -Login
            Assert-ReleaseTest ($fixture.Logins -eq 1 -and $fixture.GitHubCalls -eq 1 -and $fixture.Builds -eq 0 -and $fixture.TestsRun -eq 0) 'Login performed publication work.'
        }
        'LoginFailure' { Invoke-ReleaseFixture 'gh failed' -Login }
        'LoginWhatIf' {
            Invoke-ReleaseFixture -Login -Preview
            Assert-ReleaseTest ($fixture.GitHubCalls -eq 0 -and $fixture.Builds -eq 0 -and $fixture.TestsRun -eq 0) 'Login WhatIf performed work.'
        }
        { $_ -in 'DirtySource', 'UntrackedSource', 'ChangedDuringBuild' } { Invoke-ReleaseFixture 'Commit all source changes' }
        { $_ -in 'WrongRepository', 'MultiplePushTargets' } { Invoke-ReleaseFixture 'origin must point' }
        'TestFailure' { Invoke-ReleaseFixture 'dotnet failed' }
        'WrongBuildVersion' { Invoke-ReleaseFixture 'does not match the release version' }
        'VerificationFailure' { Invoke-ReleaseFixture 'Fixture verification failed' }
        'ExistingRelease' { Invoke-ReleaseFixture 'already published' }
        'OlderVersion' { Invoke-ReleaseFixture 'must be newer' }
        'WrongTag' { Invoke-ReleaseFixture 'does not point to HEAD' }
        'AnnotatedTag' {
            Invoke-ReleaseFixture
            Assert-ReleaseTest $fixture.Published 'An existing matching annotated tag could not be released.'
        }
        'CorruptUpload' { Invoke-ReleaseFixture 'Uploaded assets do not match' }
        'RetryUpload' {
            Invoke-ReleaseFixture 'gh failed'
            Assert-ReleaseTest $fixture.Draft.draft 'A failed upload did not leave a draft.'
            $fixture.UploadFailure = $false
            Invoke-ReleaseFixture
            Assert-ReleaseTest ($fixture.Published -and $fixture.Creates -eq 1) 'Retry did not reuse the existing draft.'
        }
        'WhatIf' {
            Invoke-ReleaseFixture -Preview
            Assert-ReleaseTest ($fixture.GitHubCalls -eq 0 -and $fixture.Builds -eq 0 -and $fixture.TestsRun -eq 0) 'WhatIf performed work.'
        }
        'Success' {
            Invoke-ReleaseFixture
            $remoteCommit = & $realGit --git-dir $fixture.Remote rev-parse refs/tags/v1.2.3
            $branchCommit = & $realGit --git-dir $fixture.Remote rev-parse refs/heads/main
            Assert-ReleaseTest ($fixture.Published -and $remoteCommit -eq $fixture.Commit -and $branchCommit -eq $fixture.Commit) 'The published tag or branch does not match the built commit.'
            $remoteTags = @(& $realGit --git-dir $fixture.Remote for-each-ref '--format=%(refname)' refs/tags)
            Assert-ReleaseTest ($remoteTags.Count -eq 1 -and $remoteTags[0] -eq 'refs/tags/v1.2.3') 'An unrelated local tag was pushed.'
        }
    }
    if ($scenario -notin @('Success', 'AnnotatedTag', 'RetryUpload', 'CorruptUpload')) {
        $remoteBranches = & $realGit --git-dir $fixture.Remote for-each-ref refs/heads
        Assert-ReleaseTest (!$remoteBranches) 'A failed preflight, build or verification pushed source changes.'
    }
    $results.Add($scenario)
}
Write-Host "Passed $($results.Count) release script scenarios (local Git only; GitHub and builds simulated). Fixtures: $runRoot"
