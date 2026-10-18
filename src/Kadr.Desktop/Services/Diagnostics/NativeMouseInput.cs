using System.Runtime.InteropServices;

namespace Kadr.Desktop.Services.Diagnostics;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeMouseInput
{
    internal uint Type;
    internal uint Padding;
    internal int X;
    internal int Y;
    internal uint Data;
    internal uint Flags;
    internal uint Time;
    internal nuint ExtraInformation;
}
