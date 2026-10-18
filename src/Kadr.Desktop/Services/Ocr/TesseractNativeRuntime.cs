using System.Runtime.InteropServices;

using Tesseract;

namespace Kadr.Desktop.Services.Ocr;

internal static class TesseractNativeRuntime
{
    private static readonly Lazy<string> NativeDirectory = new(InitializeCore);

    internal static void Initialize()
    {
        _ = NativeDirectory.Value;
    }

    private static string InitializeCore()
    {
        string paths = AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") as string ?? string.Empty;
        string directory = paths.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Append(AppContext.BaseDirectory)
            .FirstOrDefault(path => File.Exists(Path.Combine(path, "x64", "tesseract50.dll")))
            ?? throw new DllNotFoundException("The bundled x64 OCR libraries could not be located in the runtime native search directories.");

        foreach (string name in new string[] { "vcruntime140.dll", "vcruntime140_1.dll", "msvcp140.dll" })
        {
            string runtime = Path.Combine(directory, name);
            if (File.Exists(runtime))
            {
                NativeLibrary.Load(runtime);
            }
        }
        TesseractEnviornment.CustomSearchPath = directory;
        return directory;
    }
}
