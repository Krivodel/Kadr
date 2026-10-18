using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

using Kadr.Desktop.Services.Capture;
using Kadr.Desktop.Services.Input;

namespace Kadr.Desktop;

internal static class Program
{
    [STAThread]
    internal static int Main(string[] args)
    {
        if ((args.Length > 1) && args[0] == "--export-licenses")
        {
            Services.DistributionNotices.ExportAsync(args[1], CancellationToken.None).GetAwaiter().GetResult();
            return 0;
        }

        if ((args.Length > 1) && args[0] == "--distribution-probe")
        {
            return Services.Diagnostics.DistributionProbe.RunAsync(args[1], CancellationToken.None).GetAwaiter().GetResult();
        }

        if ((args.Length > 1) && string.Equals(args[0], "--render-probe", StringComparison.Ordinal))
        {
            return Services.Rendering.RenderingProbe.Run(args[1]);
        }

        if ((args.Length > 0) && string.Equals(args[0], "--capture-probe", StringComparison.Ordinal))
        {
            return CaptureProbe.RunAsync(args, CancellationToken.None).GetAwaiter().GetResult();
        }

        if ((args.Length > 1) && args[0] is "--editor-probe" or "--settings-probe")
        {
            return RunApplication(args);
        }

        using SingleInstanceGuard instance = new();

        if (!instance.IsPrimary)
        {
            return 0;
        }

        return RunApplication(args);
    }

    private static int RunApplication(string[] args)
    {
        int result = AppBuilder.Configure<App>().UsePlatformDetect().UseHarfBuzz()
            .With(new Win32PlatformOptions
            {
                CompositionMode = new Win32CompositionMode[]
                {
                    Win32CompositionMode.LowLatencyDxgiSwapChain,
                    Win32CompositionMode.WinUIComposition,
                    Win32CompositionMode.DirectComposition,
                    Win32CompositionMode.RedirectionSurface
                }
            })
            .StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);

        if (Application.Current is App application)
        {
            application.DisposeAsync().GetAwaiter().GetResult();
        }

        return result;
    }
}
