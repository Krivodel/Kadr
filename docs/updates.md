# Updates

## Release

1. Set `<Version>` in `src/Kadr.Desktop/Kadr.Desktop.csproj` and run tests.
2. Run `./eng/PrepareRelease.ps1`.
3. Upload `Kadr-win-x64.exe`, `Kadr-win-x64.exe.sha256` and `Kadr-win-x64.exe.sig` from `artifacts/release` to a public stable release in `Krivodel/Kadr`, tagged `v{version}`. Do not change the EXE after signing.

Use `-SkipPublish` to sign an existing build, or `-PublishDirectory artifacts/single-file-update` if the default output EXE is running.

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
