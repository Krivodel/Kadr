using Vortice.DXGI;

namespace Kadr.Desktop.Services.Capture;

internal sealed class MonitorCaptureFactory
{
    internal IReadOnlyList<IMonitorCapture> Create(CaptureBackend backend)
    {
        if ((backend == CaptureBackend.WindowsGraphicsCapture) && !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            throw new PlatformNotSupportedException("WGC cursor control requires Windows 10 version 2004.");
        }

        List<IMonitorCapture> outputs = [];

        try
        {
            using IDXGIFactory1 factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();

            for (uint adapterIndex = 0; factory.EnumAdapters1(adapterIndex, out IDXGIAdapter1 adapter).Success; adapterIndex++)
            {
                using (adapter)
                {
                    for (uint outputIndex = 0; adapter.EnumOutputs(outputIndex, out IDXGIOutput output).Success; outputIndex++)
                    {
                        using (output)
                        {
                            if (output.Description.AttachedToDesktop)
                            {
                                outputs.Add(CreateOutput(backend, adapter, output));
                            }
                        }
                    }
                }
            }

            if (outputs.Count == 0)
            {
                throw new InvalidOperationException("No accessible desktop monitors were found.");
            }

            return outputs.AsReadOnly();
        }
        catch
        {
            foreach (IMonitorCapture output in outputs)
            {
                output.Dispose();
            }

            throw;
        }
    }

    private static IMonitorCapture CreateOutput(CaptureBackend backend, IDXGIAdapter1 adapter, IDXGIOutput output)
    {
        if ((backend == CaptureBackend.WindowsGraphicsCapture) && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            return new WindowsGraphicsOutput(MonitorDescription.Read(output));
        }

        return new DuplicationOutput(adapter, output);
    }
}
