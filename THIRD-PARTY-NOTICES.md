# Third-party components

PaddleOCR uses RapidOcrNet 4.2.0 (Apache-2.0), ONNX Runtime 1.29.0 (MIT) and Clipper2 2.0.0 (BSL-1.0). The PP-OCRv5 mobile detector and orientation classifier are bundled from RapidOcrNet 4.2.0. The East Slavic recognizer is from RapidAI/RapidOCR model release v3.10.0 (`eslav_PP-OCRv5_rec_mobile.onnx`, SHA-256 `08705d6721849b1347d26187f15a5e362c431963a2a62bfff4feac578c489aab`); its character dictionary is extracted from the model metadata. All models are included for offline recognition and verified against the embedded `paddle-models.json` manifest. Their licenses and notices are included in `LICENSES`.

Kadr uses Avalonia (MIT), CommunityToolkit.Mvvm (MIT), Microsoft .NET and Extensions libraries (MIT), SkiaSharp (MIT, with Skia third-party components), Vortice.Windows (MIT), SharpGen.Runtime (MIT).

Interface translations use Krivodeling.Localization.Avalonia 1.0.1 (MIT) and Lang.Avalonia 12.0.5.2 (MIT). Their license notices are included in `LICENSES`.

Portable updates use StageKit.Updatum, StageKit.Primitives and StageKit.Runtime 0.4.1 (MIT), Octokit 14.0.0 (MIT), NSec.Cryptography 26.4.0 (MIT) and libsodium 1.0.22 (ISC). Their licenses and NSec's third-party notice are included in `LICENSES`.

Settings scrolling adapts the smooth scrolling behavior from AtomicArt (MIT, Copyright 2026 Krivodel). The same MIT terms and copyright notice are included in `LICENSE`.

The portable distribution also contains Microsoft Visual C++ app-local redistributable runtime DLLs supplied with Visual Studio. These retain Microsoft's licensing terms. The publishing script obtains these from the Visual Studio redistributable directory rather than copying operating-system DLLs.

Distributable Code for Microsoft Visual Studio 2022 (Includes Utilities & BuildServer Files): https://aka.ms/vs/17/redist.txt. This is the redistributable file list; the applicable Microsoft Software License Terms govern redistribution.

Upstream projects and notices:

- https://github.com/AvaloniaUI/Avalonia
- https://github.com/CommunityToolkit/dotnet
- https://github.com/Krivodel/Krivodeling.Localization.Avalonia
- https://github.com/dotnet9/Lang.Avalonia
- https://github.com/dotnet/runtime
- https://github.com/mono/SkiaSharp
- https://github.com/amerkoleci/Vortice.Windows
- https://github.com/SharpGenTools/SharpGenTools

Pica was inspected as an architectural reference. Its application source is not included in Kadr. The shared localization library is referenced as its own NuGet package.
