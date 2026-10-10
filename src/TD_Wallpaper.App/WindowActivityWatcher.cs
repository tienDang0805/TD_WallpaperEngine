using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace TD_Wallpaper.App;

internal sealed class WindowActivityWatcher : IDisposable
{
    private delegate void WinEventProc(nint hook, uint evt, nint hwnd, int idObject, int idChild, uint thread, uint eventTime);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventProc callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint hook);
    private readonly List<nint> _hooks = [];
    private readonly Dispatcher _dispatcher;
    private readonly Action<uint> _changed;
    private readonly WinEventProc _callback;
    private GCHandle _root;
    private bool _queued, _disposed;
    private readonly HashSet<nint> _knownWindows = [];
    private uint _eventTime;
    internal int HookCount => _hooks.Count;
    internal WindowActivityWatcher(Dispatcher dispatcher, Action<uint> changed)
    {
        _dispatcher = dispatcher; _changed = changed; _callback = OnEvent;
        _root = GCHandle.Alloc(_callback);
        try
        {
            NativeDesktop.EnumWindows((hwnd, _) => { _knownWindows.Add(hwnd); return true; }, 0);
            Add(0x0003, 0x0003); // foreground, including our own app becoming active
            Add(0x0016, 0x0017); // minimize start/end
            Add(0x8000, 0x800B); // destroy/show/hide/state/location, filtered to top-level windows
        }
        catch { Dispose(); throw; }
    }
    private void Add(uint min, uint max)
    {
        // OUTOFCONTEXT: callback runs on this dispatcher thread, no code injection.
        var hook = SetWinEventHook(min, max, 0, _callback, 0, 0, 0);
        if (hook == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Window activity hook unavailable.");
        _hooks.Add(hook);
    }
    private void OnEvent(nint hook, uint evt, nint hwnd, int idObject, int idChild, uint thread, uint eventTime)
    {
        if (_disposed || _dispatcher.HasShutdownStarted) return;
        if (evt >= 0x8000)
        {
            if (idObject != 0 || idChild != 0 || hwnd == 0) return;
            if (evt == 0x8001) { if (!_knownWindows.Remove(hwnd)) return; }
            else { if (NativeDesktop.GetAncestor(hwnd, 2) != hwnd) return; _knownWindows.Add(hwnd); }
        }
        if (evt >= 0x8000)
        {
            NativeDesktop.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == Environment.ProcessId) return;
        }
        _eventTime = eventTime;
        if (_queued) return;
        _queued = true;
        _dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() =>
        {
            _queued = false;
            if (!_disposed) _changed(_eventTime);
        }));
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var hook in _hooks) UnhookWinEvent(hook);
        _hooks.Clear(); _knownWindows.Clear();
        if (_root.IsAllocated) _root.Free();
    }
}