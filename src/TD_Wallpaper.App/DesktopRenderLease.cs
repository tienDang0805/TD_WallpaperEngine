using System.Security.Cryptography;
using System.Text;

namespace TD_Wallpaper.App;

internal sealed class DesktopBusyException() : IOException(L.Text("Một bản TD_Wallpaper khác đang phát trên màn hình này. Thoát bản đó từ khay hệ thống rồi thử lại."));

// Library mutexes protect data. This separate lease protects the shared desktop
// across versions and --data-dir libraries. An abandoned lease is recoverable.
internal sealed class DesktopRenderLease : IDisposable
{
    private readonly Mutex _mutex;
    private bool _owned;
    private DesktopRenderLease(Mutex mutex) { _mutex = mutex; _owned = true; }
    internal static DesktopRenderLease Acquire(string displayId, Func<bool>? legacyConflict = null)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(displayId.ToUpperInvariant())))[..20];
        var mutex = new Mutex(false, @"Local\TDWallpaper-Desktop-" + hash);
        var acquired = false;
        try
        {
            try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new DesktopBusyException();
            // Old releases predate the lease, so also inspect existing desktop
            // child windows before creating/reordering any Explorer window.
            if ((legacyConflict ?? (() => NativeDesktop.OtherRenderer(displayId)))()) throw new DesktopBusyException();
            return new(mutex);
        }
        catch { if (acquired) mutex.ReleaseMutex(); mutex.Dispose(); throw; }
    }
    public void Dispose()
    {
        if (!_owned) return;
        _owned = false; _mutex.ReleaseMutex(); _mutex.Dispose();
    }
}
