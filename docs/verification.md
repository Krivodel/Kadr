# Проверка перед выпуском

Команды сборки, тестов и проверки автономного EXE — в [README](../Readme.md). Запускать на Windows из корня проекта. `VerifySingleFile.ps1` проверяет автономный запуск, русский/английский OCR без сети, повторное использование кэша, окно настроек, DXGI, GPU-рендерер и экспорт лицензий. Отчёт: `artifacts/single-file-verification/verification.json`.

## Отдельные диагностические команды

После публикации EXE:

```powershell
New-Item -ItemType Directory -Force ./artifacts/probes | Out-Null
$kadr = './artifacts/single-file/Kadr.exe'
& $kadr --capture-probe ./artifacts/probes/capture.png # DXGI по нажатию
& $kadr --capture-probe ./artifacts/probes/wgc.png --wgc # WGC
& $kadr --capture-probe ./artifacts/probes/benchmark.png --continuous --iterations=30
& $kadr --capture-probe ./artifacts/probes/freshness.json --verify-freshness # Свежесть DXGI
& $kadr --capture-probe ./artifacts/probes/lifecycle.json --exercise-lifecycle --iterations=3
& $kadr --distribution-probe ./artifacts/probes/distribution.json # OCR без сети и нативные DLL
& $kadr --render-probe ./artifacts/probes/render.json # GPU-рендерер
& $kadr --editor-probe ./artifacts/probes/editor.json # Производительность редактора
& $kadr --settings-probe ./artifacts/probes/settings.json # Окно настроек
& $kadr --silent --smoke-test # Запуск и завершение
& $kadr --export-licenses ./artifacts/probes/licenses
```

Обычный `--capture-probe` сохраняет PNG и соседний `.png.json` с замерами; снимок делается после пятисекундного замера фоновой нагрузки. `--wgc` и `--continuous` можно сочетать. `--verify-freshness` всегда проверяет DXGI по нажатию, а `--exercise-lifecycle` перебирает оба механизма и режима.

## Ручная проверка

Проверить Windows 10 1809 и Windows 11, полноэкранные приложения, несколько мониторов со смешанным DPI/HDR, поворот и отключение монитора, сон и смену видеорежима.

Предупреждения Avalonia AVLN3001 для `EditorWindow` и `ColorPickerWindow` ожидаемы: окна создаются конструкторами с параметрами, а не через URI-загрузчик.