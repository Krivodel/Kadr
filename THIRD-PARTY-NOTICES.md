# Third-party components

Kadr uses Avalonia (MIT), CommunityToolkit.Mvvm (MIT), Microsoft .NET and Extensions libraries (MIT), SkiaSharp (MIT, with Skia third-party components), Vortice.Windows (MIT), SharpGen.Runtime (MIT), the Tesseract .NET wrapper (Apache-2.0), Tesseract OCR (Apache-2.0), Leptonica (BSD-2-Clause) and tessdata_best models (Apache-2.0).

Interface translations use Krivodeling.Localization.Avalonia 1.0.1 (MIT) and Lang.Avalonia 12.0.5.2 (MIT). Their license notices are included in `LICENSES`.

Portable updates use StageKit.Updatum, StageKit.Primitives and StageKit.Runtime 0.4.1 (MIT), Octokit 14.0.0 (MIT), NSec.Cryptography 26.4.0 (MIT) and libsodium 1.0.22 (ISC). Their licenses and NSec's third-party notice are included in `LICENSES`.

Settings scrolling adapts the smooth scrolling behavior from AtomicArt (MIT, Copyright 2026 Krivodel). Its license is included as `LICENSES/AtomicArt.txt`.

Tesseract native DLLs are supplied by the Tesseract 5.2.0 NuGet package. The English and Russian model files are pinned to tessdata_best Git tree e12c65a915945e4c28e237a9b52bc4a8f39a0cec. The full model manifest is embedded in the application and verifies Git blob checksums before installation.

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
- https://github.com/charlesw/tesseract
- https://github.com/tesseract-ocr/tesseract
- https://github.com/DanBloomberg/leptonica
- https://github.com/tesseract-ocr/tessdata_best

Pica was inspected as an architectural reference. Its application source is not included in Kadr. The shared localization library is referenced as its own NuGet package.
