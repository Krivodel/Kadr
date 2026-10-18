# Updates

## Release

Install the Windows amd64 MSI from [GitHub CLI releases](https://github.com/cli/cli/releases/latest), reopen the terminal, then run `./eng/Release.ps1 -Login` once.

Login and publication use GitHub CLI's saved credentials. `GH_TOKEN` and `GITHUB_TOKEN` are excluded only while running GitHub CLI and Git commands, then restored even on failure. Windows environment settings are unchanged.

1. Set a new stable `<Version>` (`X.Y.Z`) in `src/Kadr.Desktop/Kadr.Desktop.csproj` and commit the changes.
2. Run `./eng/Release.ps1` in Windows PowerShell 5.1 or PowerShell 7.

The script tests, builds, signs and verifies the single-file EXE, pushes the current branch and `v{version}` tag to `origin`, uploads the EXE, SHA-256 and signature, verifies the uploaded hashes, then publishes a stable GitHub Release in `Krivodel/Kadr`. Release notes are generated from GitHub; use `-NotesFile ./notes.md` for your own notes or `-CrtDirectory` for a custom CRT directory. `-WhatIf` previews the operation without building or publishing.

If an upload fails, rerun from the same commit to resume the draft. Published versions are never overwritten. Do not change the EXE after signing.

For local assets without uploading, use `./eng/PrepareRelease.ps1` (`-SkipPublish` to sign an existing build, `-PublishDirectory` for another build directory).

`./eng/VerifyRelease.ps1` tests the release workflow with local Git repositories and simulated GitHub responses, without publishing anything.

Kadr checks releases at startup and every six hours. Updates require a published single-file EXE in a writable directory.

## Signing key

The private key is stored at `%LOCALAPPDATA%\Kadr\ReleaseSigning\private-key.bin`, encrypted for the current Windows account and machine. Export a portable backup before moving to another machine or reinstalling Windows:

```powershell
dotnet run --project eng/Kadr.ReleaseTool -c Release -- export-key X:\secure-backup\kadr-update.key
dotnet run --project eng/Kadr.ReleaseTool -c Release -- import-key X:\secure-backup\kadr-update.key
```

Keep the private key and its backup outside Git and release assets. Preserve `src/Kadr.Desktop/Resources/Updates/public-key.txt`: replacing it breaks trust for existing installations.

## Recovery and verification

If an update exits Kadr without restarting it, launch Kadr again to restore the pending screenshot. The encrypted recovery session is stored in `%LOCALAPPDATA%\Kadr\UpdateSession` and deleted after the editor reopens successfully.

```powershell
./eng/VerifyUpdates.ps1 # Add -KeepFiles to retain test builds
```

Checks EXE replacement, restart and session restoration locally. Report: `artifacts/update-install-verification.json`.
