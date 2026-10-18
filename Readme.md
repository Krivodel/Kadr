**English** | [Русский](Readme.ru.md)

# <img src="src/Kadr.Desktop/Assets/AppIcon.png" alt="Kadr" width="32" height="32"> Kadr

Screenshot tool for Windows 10 1809+ / Windows 11, x64.

## Development

Use the .NET SDK specified in `global.json`. Run commands from the project root:

```powershell
dotnet build Kadr.slnx -c Release
dotnet run --project src/Kadr.Desktop
dotnet test Kadr.slnx -c Release
./eng/Publish.ps1 # Self-contained EXE
./eng/PrepareRelease.ps1 # Build and sign GitHub release assets
./eng/Release.ps1 -Login # Sign in to GitHub once
./eng/Release.ps1 # Test, build, sign and publish a GitHub Release
./eng/VerifySingleFile.ps1 -ExecutablePath ./artifacts/single-file/Kadr.exe
./eng/Clean.ps1 -WhatIf # List files; omit -WhatIf to delete
```

`Publish.ps1` creates `artifacts/single-file/Kadr.exe`. Visual C++ x64 CRT files from Visual Studio 2022 are required (`-CrtDirectory` for another directory).

`Clean.ps1` requires the EXE, `artifacts/Kadr-win-x64.zip` and a successful `artifacts/verification.json` report with matching hashes.

[Diagnostics](docs/verification.md) · [Releases and signing key](docs/updates.md)
