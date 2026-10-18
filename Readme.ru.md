[English](Readme.md) | **Русский**

# <img src="src/Kadr.Desktop/Assets/AppIcon.png" alt="Kadr" width="32" height="32"> Kadr

Скриншотер для Windows 10 1809+ / Windows 11, x64.

# Команды из корня проекта

```powershell
dotnet build Kadr.slnx -c Release # Сборка для разработки
dotnet run --project src/Kadr.Desktop # Запуск
dotnet test Kadr.slnx -c Release # Тесты
./eng/Publish.ps1 # Автономный EXE
./eng/VerifySingleFile.ps1 -ExecutablePath ./artifacts/single-file/Kadr.exe
```

Публикация создаёт автономный `artifacts/single-file/Kadr.exe`. Нужны Visual C++ x64 CRT из Visual Studio 2022; другой каталог можно указать через `-CrtDirectory`.

Очистка накопленных результатов требует проверенной поставки: EXE, `artifacts/Kadr-win-x64.zip` и соответствующего отчёта `artifacts/verification.json`.

```powershell
./eng/Clean.ps1 -WhatIf # Показать список; без -WhatIf — удалить
```

[Диагностические команды и проверка перед выпуском](docs/verification.md).