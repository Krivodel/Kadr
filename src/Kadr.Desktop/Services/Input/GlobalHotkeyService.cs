using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;

using Kadr.Desktop.Services.Settings;

namespace Kadr.Desktop.Services.Input;

public sealed class GlobalHotkeyService : IAsyncDisposable
{
    public event EventHandler? Activated;

    private const uint CommandMessage = 0x8001;
    private const uint HotkeyMessage = 0x0312;
    private readonly ConcurrentQueue<Action> _commands = new();
    private readonly TaskCompletionSource<uint> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Dictionary<int, ShortcutGesture> _registrations = [];
    private int _nextRegistration;
    private nint _mouseHook;
    private HookCallback? _mouseCallback;
    private ShortcutGesture[] _mouseGestures = Array.Empty<ShortcutGesture>();
    private int _disposed;

    public GlobalHotkeyService()
    {
        Thread thread = new(Run)
        {
            Name = "Kadr global hotkey",
            IsBackground = true
        };
        thread.Start();
    }

    public Task SetAsync(ShortcutGesture gesture, CancellationToken ct)
    {
        return SetAsync(new ShortcutGesture[] { gesture }, ct);
    }

    public async Task SetAsync(IReadOnlyList<ShortcutGesture> gestures, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(gestures);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        uint thread = await _ready.Task.WaitAsync(ct).ConfigureAwait(false);
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _commands.Enqueue(() =>
        {
            try
            {
                ct.ThrowIfCancellationRequested();

                Dictionary<int, ShortcutGesture> additions = [];
                bool addedMouseHook = false;
                try
                {
                    foreach (ShortcutGesture gesture in gestures.Where(gesture => !gesture.IsEmpty && !MouseShortcut.IsMouseKey(gesture.Key)).Distinct())
                    {
                        if (_registrations.ContainsValue(gesture))
                        {
                            continue;
                        }
                        int identifier = ++_nextRegistration;
                        if (!RegisterHotKey(0, identifier, (uint)gesture.Modifiers | 0x4000, (uint)gesture.Key))
                        {
                            throw new Win32Exception(Marshal.GetLastWin32Error(), "The requested screenshot shortcut is unavailable.");
                        }
                        additions.Add(identifier, gesture);
                    }
                    if (gestures.Any(gesture => MouseShortcut.IsMouseKey(gesture.Key)) && _mouseHook == 0)
                    {
                        _mouseCallback = OnMouse;
                        _mouseHook = SetWindowsHookExW(14, _mouseCallback, GetModuleHandleW(null), 0);
                        if (_mouseHook == 0)
                        {
                            throw new Win32Exception(Marshal.GetLastWin32Error());
                        }
                        addedMouseHook = true;
                    }
                }
                catch
                {
                    foreach (int identifier in additions.Keys)
                    {
                        UnregisterHotKey(0, identifier);
                    }
                    if (addedMouseHook)
                    {
                        UnhookWindowsHookEx(_mouseHook);
                        _mouseHook = 0;
                    }
                    throw;
                }
                foreach (int identifier in _registrations.Keys.Where(identifier => !gestures.Contains(_registrations[identifier])).ToArray())
                {
                    UnregisterHotKey(0, identifier);
                    _registrations.Remove(identifier);
                }
                foreach (KeyValuePair<int, ShortcutGesture> addition in additions)
                {
                    _registrations.Add(addition.Key, addition.Value);
                }
                _mouseGestures = gestures.Where(gesture => MouseShortcut.IsMouseKey(gesture.Key)).Distinct().ToArray();
                if (_mouseGestures.Length == 0 && _mouseHook != 0)
                {
                    UnhookWindowsHookEx(_mouseHook);
                    _mouseHook = 0;
                }

                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        });
        PostThreadMessageW(thread, CommandMessage, 0, 0);
        await completion.Task.ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            uint thread = await _ready.Task.ConfigureAwait(false);
            PostThreadMessageW(thread, 0x0012, 0, 0);
            await _finished.Task.ConfigureAwait(false);
        }
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint window, int identifier, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint window, int identifier);
    [DllImport("user32.dll")] private static extern int GetMessageW(out NativeMessage message, nint window, uint minimum, uint maximum);
    [DllImport("user32.dll")] private static extern bool PeekMessageW(out NativeMessage message, nint window, uint minimum, uint maximum, uint flags);
    [DllImport("user32.dll")] private static extern bool PostThreadMessageW(uint thread, uint message, nuint parameter, nint data);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

    private void Run()
    {
        PeekMessageW(out _, 0, 0, 0, 0);
        _ready.SetResult(GetCurrentThreadId());

        try
        {
            while (GetMessageW(out NativeMessage message, 0, 0, 0) > 0)
            {
                if (message.Identifier == CommandMessage)
                {
                    while (_commands.TryDequeue(out Action? command))
                    {
                        command();
                    }
                }
                else if (message.Identifier == HotkeyMessage && _registrations.ContainsKey((int)message.Parameter))
                {
                    Activated?.Invoke(this, EventArgs.Empty);
                }
            }
        }
        finally
        {
            foreach (int identifier in _registrations.Keys)
            {
                UnregisterHotKey(0, identifier);
            }
            if (_mouseHook != 0)
            {
                UnhookWindowsHookEx(_mouseHook);
            }
            GC.KeepAlive(_mouseCallback);

            _finished.SetResult();
        }
    }

    private nint OnMouse(int code, nint message, nint data)
    {
        if (code >= 0 && MouseShortcut.GetPressedKey(message) is int key && key != 0 && _mouseGestures.Contains(new ShortcutGesture(key, NativeModifiers.Get())))
        {
            Activated?.Invoke(this, EventArgs.Empty);
            return 1;
        }
        return CallNextHookEx(_mouseHook, code, message, data);
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookExW(int hook, HookCallback callback, nint module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandleW(string? module);
    private delegate nint HookCallback(int code, nint message, nint data);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        internal nint Window; internal uint Identifier; internal nuint Parameter; internal nint Data;
        internal uint Time; internal int X; internal int Y; internal uint Private;
    }
}
