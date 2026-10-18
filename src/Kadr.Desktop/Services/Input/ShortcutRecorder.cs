using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

using Kadr.Desktop.Services.Capture;
using Kadr.Desktop.Services.Settings;

namespace Kadr.Desktop.Services.Input;

internal sealed class ShortcutRecorder : IDisposable
{
    internal ShortcutRecordingSession Session { get; }
    private readonly HookCallback _callback;
    private readonly nint _hook;
    private readonly HookCallback _mouseCallback;
    private readonly nint _mouseHook;

    internal ShortcutRecorder(ShortcutRecordingSession session)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        _callback = OnKeyboard;
        _mouseCallback = OnMouse;
        _hook = SetWindowsHookExW(13, _callback, GetModuleHandleW(null), 0);

        if (_hook == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        _mouseHook = SetWindowsHookExW(14, _mouseCallback, GetModuleHandleW(null), 0);
        if (_mouseHook == 0)
        {
            int error = Marshal.GetLastWin32Error();
            UnhookWindowsHookEx(_hook);
            throw new Win32Exception(error);
        }

    }

    public void Dispose()
    {
        UnhookWindowsHookEx(_hook);
        UnhookWindowsHookEx(_mouseHook);
        GC.KeepAlive(_callback);
        GC.KeepAlive(_mouseCallback);
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookExW(int hook, HookCallback callback, nint module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandleW(string? module);

    private nint OnKeyboard(int code, nint message, nint data)
    {
        if ((code >= 0) && ((message == 0x101) || (message == 0x105))
            && Session.Release(Marshal.ReadInt32(data), Stopwatch.GetElapsedTime(0)))
        {
            return 1;
        }

        if ((code >= 0) && ((message == 0x100) || (message == 0x104)))
        {
            int key = Marshal.ReadInt32(data);

            if (Session.Press(new ShortcutGesture(key, NativeModifiers.Get()), Stopwatch.GetElapsedTime(0)))
            {
                return 1;
            }
        }

        return CallNextHookEx(_hook, code, message, data);
    }

    private nint OnMouse(int code, nint message, nint data)
    {
        if (code >= 0 && MouseShortcut.GetPressedKey(message) is int key && key != 0)
        {
            Session.Press(new ShortcutGesture(key, NativeModifiers.Get()), Stopwatch.GetElapsedTime(0));
            return 1;
        }
        if (code >= 0 && Session.Release(MouseShortcut.GetReleasedKey(message), Stopwatch.GetElapsedTime(0)))
        {
            return 1;
        }
        return CallNextHookEx(_mouseHook, code, message, data);
    }

    private delegate nint HookCallback(int code, nint message, nint data);
}
