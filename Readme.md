**English** | [Русский](Readme.ru.md)

# <img src="src/Kadr.Desktop/Assets/AppIcon.png" alt="Kadr" width="32" height="32"> Kadr

Screenshot tool for Windows 10 1809+ / Windows 11, x64.

# Commands to run from the project root

```powershell
dotnet build Kadr.slnx -c Release # Development build
dotnet run --project src/Kadr.Desktop # Run
dotnet test Kadr.slnx -c Release # Tests
./eng/Publish.ps1 # Self-contained EXE
./eng/VerifySingleFile.ps1 -ExecutablePath ./artifacts/single-file/Kadr.exe
python ./eng/GenerateIcon.py # Generate the icon and convert SVG to PNG/ICO; requires Pillow
```

Publishing creates a self-contained `artifacts/single-file/Kadr.exe`. Visual C++ x64 CRT files from Visual Studio 2022 are required; use `-CrtDirectory` to specify a different directory.

Cleaning generated files requires a verified distribution: the EXE, `artifacts/Kadr-win-x64.zip`, and the corresponding `artifacts/verification.json` report.

```powershell
./eng/Clean.ps1 -WhatIf # List files; omit -WhatIf to delete them
```

[Diagnostic commands and pre-release verification](docs/verification.md).