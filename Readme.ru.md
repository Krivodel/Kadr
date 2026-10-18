[English](Readme.md) | **Русский**

[![Скачать для Windows](https://badgen.net/badge/icon/%D0%A1%D0%BA%D0%B0%D1%87%D0%B0%D1%82%D1%8C%20%D0%B4%D0%BB%D1%8F%20Windows?icon=windows&label)](https://github.com/Krivodel/Kadr/releases/latest/download/Kadr-win-x64.exe)

# <img src="src/Kadr.Desktop/Assets/AppIcon.png" alt="Kadr" width="32" height="32"> Kadr

Скриншотер для Windows 10 1809+ / Windows 11, x64.

## Разработка

Используй .NET SDK из `global.json`. Команды выполняются из корня проекта:

```powershell
dotnet build Kadr.slnx -c Release
dotnet run --project src/Kadr.Desktop
dotnet test Kadr.slnx -c Release
./eng/Publish.ps1 # Автономный EXE
./eng/PrepareRelease.ps1 # Сборка и подпись файлов для GitHub Releases
./eng/Release.ps1 -Login # Однократный вход в GitHub
./eng/Release.ps1 # Тесты, сборка, подпись и публикация GitHub Release
./eng/VerifySingleFile.ps1 -ExecutablePath ./artifacts/single-file/Kadr.exe
./eng/Clean.ps1 -WhatIf # Показать список; без -WhatIf — удалить
```

`Publish.ps1` создаёт `artifacts/single-file/Kadr.exe`. Нужны Visual C++ x64 CRT из Visual Studio 2022 (`-CrtDirectory` для другого каталога).

`Clean.ps1` требует EXE, `artifacts/Kadr-win-x64.zip` и успешный отчёт `artifacts/verification.json` с совпадающими хешами.

[Диагностика](docs/verification.md) · [Релизы и ключ подписи](docs/updates.md)
