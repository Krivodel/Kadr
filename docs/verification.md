# Pre-release verification

Build, test, and self-contained EXE verification commands are in the [README](../Readme.md). Run them on Windows from the project root. `VerifySingleFile.ps1` checks standalone startup, offline Russian/English OCR, cache reuse, the settings window, DXGI, GPU rendering, and license export. Report: `artifacts/single-file-verification/verification.json`.

## Individual diagnostic commands

After publishing the EXE:

```powershell
New-Item -ItemType Directory -Force ./artifacts/probes | Out-Null
$kadr = './artifacts/single-file/Kadr.exe'
& $kadr --capture-probe ./artifacts/probes/capture.png # DXGI on demand
& $kadr --capture-probe ./artifacts/probes/wgc.png --wgc # WGC
& $kadr --capture-probe ./artifacts/probes/benchmark.png --continuous --iterations=30
& $kadr --capture-probe ./artifacts/probes/freshness.json --verify-freshness # DXGI frame freshness
& $kadr --capture-probe ./artifacts/probes/lifecycle.json --exercise-lifecycle --iterations=3
& $kadr --distribution-probe ./artifacts/probes/distribution.json # Offline OCR and native DLLs
& $kadr --render-probe ./artifacts/probes/render.json # GPU renderer
& $kadr --editor-probe ./artifacts/probes/editor.json # Editor performance
& $kadr --settings-probe ./artifacts/probes/settings.json # Settings window
& $kadr --silent --smoke-test # Startup and shutdown
& $kadr --export-licenses ./artifacts/probes/licenses
```

A regular `--capture-probe` saves a PNG and an accompanying `.png.json` with measurements; the screenshot is taken after measuring background load for five seconds. `--wgc` and `--continuous` can be combined. `--verify-freshness` always checks DXGI on demand, while `--exercise-lifecycle` cycles through both capture backends and modes.

## Manual verification

Check Windows 10 1809 and Windows 11, fullscreen applications, multiple monitors with mixed DPI/HDR, monitor rotation and disconnection, sleep, and display mode changes.

Avalonia AVLN3001 warnings for `EditorWindow` and `ColorPickerWindow` are expected: the windows are created using parameterized constructors rather than the URI loader.