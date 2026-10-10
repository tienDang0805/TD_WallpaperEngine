using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace TD_Wallpaper.App;

internal interface ITaskbarBackend : IDisposable
{
    Task EnableAsync(CancellationToken token);
    Task DisableAsync();
    void Refresh();
}

// Own only this app's changes/helper. Closing the library window keeps the
// effect active in the tray; disabling or exiting restores the shell appearance.
internal sealed class TaskbarTransparency : IDisposable
{
    private readonly ITaskbarBackend _backend;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;
    internal bool Enabled { get; private set; }
    internal TaskbarTransparency(string dataDirectory) : this(OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)
        ? new TranslucentTaskbarBackend(dataDirectory) : new ClassicTaskbarBackend()) { }
    internal TaskbarTransparency(ITaskbarBackend backend) => _backend = backend;
    internal async Task SetEnabledAsync(bool enabled)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (enabled == Enabled) return;
            if (enabled)
            {
                // File verification/copying and process warm-up never block WPF.
                try { await Task.Run(() => _backend.EnableAsync(_lifetime.Token), _lifetime.Token).ConfigureAwait(false); _lifetime.Token.ThrowIfCancellationRequested(); Enabled = true; }
                catch { await _backend.DisableAsync().ConfigureAwait(false); throw; }
            }
            else { await _backend.DisableAsync().ConfigureAwait(false); Enabled = false; }
        }
        finally { _gate.Release(); }
    }
    internal void Refresh() { if (Enabled && !_disposed) _backend.Refresh(); }
    public void Dispose()
    {
        _lifetime.Cancel();
        _gate.Wait();
        try { if (_disposed) return; _disposed = true; _backend.Dispose(); Enabled = false; }
        finally { _gate.Release(); }
    }
}

internal sealed class TranslucentTaskbarBackend(string dataDirectory) : ITaskbarBackend
{
    private Process? _process;
    private OwnedProcessJob? _job;
    private EventWaitHandle? _lease;
    private const string ExeHash = "F933F5BF70405E13FEADBCC53883F2676B5A8A1DAE214B52C0FE0971C583A0FF";
    internal int ProcessId => _process is { HasExited: false } ? _process.Id : 0;
    internal static string Configuration => """
        {"desktop_appearance":{"accent":"clear","color":"#00000000","show_line":false,"show_peek":true},
         "visible_window_appearance":{"enabled":false},"maximized_window_appearance":{"enabled":false},
         "start_opened_appearance":{"enabled":false},"search_opened_appearance":{"enabled":false},
         "task_view_opened_appearance":{"enabled":false},"battery_saver_appearance":{"enabled":false},
         "hide_tray":true,"disable_saving":true,"verbosity":"warn"}
        """;
    public async Task EnableAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        _lease = new EventWaitHandle(false, EventResetMode.ManualReset, @"Local\TDWallpaperEngine-Taskbar", out var acquired);
        if (!acquired) throw new IOException(L.Text("Một bản app khác đang điều khiển taskbar."));
        using (var existing = Mutex.TryOpenExisting("344635E9-9AE4-4E60-B128-D53E25AB70A7", out var mutex) ? mutex : null)
            if (existing != null) throw new IOException(L.Text("TranslucentTB đang chạy. Thoát TranslucentTB trước khi bật tùy chọn này."));
        var bundle = Path.Combine(AppContext.BaseDirectory, "TaskbarTools", "TranslucentTB");
        var executable = Path.Combine(bundle, "TranslucentTB.exe");
        if (!File.Exists(executable) || Hash(executable) != ExeHash)
            throw new IOException(L.Text("Thiếu hoặc hỏng bộ hỗ trợ taskbar. Cài lại bản app đầy đủ."));
        var records = JsonSerializer.Deserialize<FileRecord[]>(File.ReadAllText(Path.Combine(bundle, "FILE-HASHES.json"))) ?? [];
        if (records.Length < 6) throw new IOException("Invalid taskbar helper manifest.");
        var target = Path.Combine(dataDirectory, "toolsets", "TranslucentTB-2026.2");
        Directory.CreateDirectory(target);
        foreach (var record in records)
        {
            token.ThrowIfCancellationRequested();
            var source = SafeChild(bundle, record.Path); var destination = SafeChild(target, record.Path);
            if (Hash(source) != record.SHA256) throw new IOException(L.Text("Thiếu hoặc hỏng bộ hỗ trợ taskbar. Cài lại bản app đầy đủ."));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (!File.Exists(destination) || Hash(destination) != record.SHA256) File.Copy(source, destination, true);
        }
        File.Copy(Path.Combine(bundle, "THIRD-PARTY.txt"), Path.Combine(target, "THIRD-PARTY.txt"), true);
        File.WriteAllText(Path.Combine(target, "settings.json"), Configuration);
        _process = Process.Start(new ProcessStartInfo(Path.Combine(target, "TranslucentTB.exe"))
        { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = target }) ?? throw new IOException("Taskbar helper did not start.");
        _job = OwnedProcessJob.Attach(_process);
        // Find the owned hidden tray window, not another user's TranslucentTB.
        var deadline = Stopwatch.StartNew();
        while (FindOwnedWindow() == 0)
        {
            if (_process.HasExited) throw new IOException(L.Text("Không bật được taskbar trong suốt. Xem log hoặc thử cài lại app."));
            if (deadline.Elapsed > TimeSpan.FromSeconds(8)) throw new TimeoutException(L.Text("Không bật được taskbar trong suốt. Xem log hoặc thử cài lại app."));
            await Task.Delay(50, token).ConfigureAwait(false);
        }
    }
    private sealed record FileRecord(string Path, string SHA256);
    private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    private static string SafeChild(string root, string relative)
    {
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Invalid taskbar helper path.");
        return path;
    }
    private nint FindOwnedWindow()
    {
        if (_process == null || _process.HasExited) return 0;
        // TrayWindow is a message-only window in the unmodified portable helper.
        foreach (var parent in new nint[] { -3, 0 })
        {
            nint previous = 0;
            while ((previous = FindWindowEx(parent, previous, "TrayWindow", null)) != 0)
            { GetWindowThreadProcessId(previous, out var pid); if (pid == _process.Id) return previous; }
        }
        return 0;
    }
    public async Task DisableAsync()
    {
        try
        {
            if (_process is { HasExited: false })
            {
                var hwnd = FindOwnedWindow(); if (hwnd != 0) PostMessage(hwnd, 0x10, 0, 0); // WM_CLOSE: upstream restores all taskbars.
                try { await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); }
                catch (TimeoutException) { _job?.Dispose(); if (!_process.HasExited) _process.Kill(); await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); }
            }
        }
        finally { _job?.Dispose(); _job = null; _process?.Dispose(); _process = null; _lease?.Dispose(); _lease = null; }
    }
    public void Refresh() { /* The helper subscribes to Explorer/theme/display events itself. */ }
    public void Dispose() => DisableAsync().GetAwaiter().GetResult();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindowEx(nint parent, nint after, string cls, string? title);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);
}

internal sealed class ClassicTaskbarBackend : ITaskbarBackend
{
    [StructLayout(LayoutKind.Sequential)] private struct Accent { public int State, Flags; public uint Color; public int Animation; }
    [StructLayout(LayoutKind.Sequential)] private struct Attribute { public int Kind; public nint Data; public nuint Size; }
    private readonly Dictionary<nint, Accent> _original = [];
    private EventWaitHandle? _lease;
    public Task EnableAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        _lease = new EventWaitHandle(false, EventResetMode.ManualReset, @"Local\TDWallpaperEngine-Taskbar", out var acquired);
        if (!acquired) throw new IOException(L.Text("Một bản app khác đang điều khiển taskbar."));
        Refresh(); if (_original.Count == 0) throw new IOException(L.Text("Không bật được taskbar trong suốt. Xem log hoặc thử cài lại app.")); return Task.CompletedTask;
    }
    public void Refresh()
    {
        foreach (var cls in new[] { "Shell_TrayWnd", "Shell_SecondaryTrayWnd" })
        {
            nint hwnd = 0;
            while ((hwnd = FindWindowEx(0, hwnd, cls, null)) != 0)
            {
                var data = Marshal.AllocHGlobal(Marshal.SizeOf<Accent>());
                try
                {
                    var attr = new Attribute { Kind = 19, Data = data, Size = (nuint)Marshal.SizeOf<Accent>() };
                    if (!_original.ContainsKey(hwnd))
                    {
                        if (!GetWindowCompositionAttribute(hwnd, ref attr)) continue;
                        _original[hwnd] = Marshal.PtrToStructure<Accent>(data);
                    }
                    Marshal.StructureToPtr(new Accent { State = 2 }, data, false);
                    if (!SetWindowCompositionAttribute(hwnd, ref attr)) throw new IOException("Taskbar composition update failed.");
                }
                finally { Marshal.FreeHGlobal(data); }
            }
        }
    }
    public Task DisableAsync()
    {
        foreach (var (hwnd, accent) in _original)
        {
            var cls = new System.Text.StringBuilder(128);
            GetClassName(hwnd, cls, cls.Capacity);
            if (cls.ToString() is not ("Shell_TrayWnd" or "Shell_SecondaryTrayWnd")) continue;
            var data = Marshal.AllocHGlobal(Marshal.SizeOf<Accent>());
            try { Marshal.StructureToPtr(accent, data, false); var attr = new Attribute { Kind = 19, Data = data, Size = (nuint)Marshal.SizeOf<Accent>() }; SetWindowCompositionAttribute(hwnd, ref attr); }
            finally { Marshal.FreeHGlobal(data); }
        }
        _original.Clear(); _lease?.Dispose(); _lease = null; return Task.CompletedTask;
    }
    public void Dispose() => DisableAsync().GetAwaiter().GetResult();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindowEx(nint parent, nint after, string cls, string? title);
    [DllImport("user32.dll")] private static extern bool GetWindowCompositionAttribute(nint window, ref Attribute data);
    [DllImport("user32.dll")] private static extern bool SetWindowCompositionAttribute(nint window, ref Attribute data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, System.Text.StringBuilder name, int length);
}
