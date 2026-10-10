using System;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TienDang.App;
using TienDang.Core;

internal static class TaskbarChecks
{
    private sealed class Backend : ITaskbarBackend
    {
        internal int Enables, Disables;
        internal bool Fail;
        internal bool Slow;
        public Task EnableAsync(CancellationToken token) { Enables++; return Slow ? Task.Delay(5000, token) : Fail ? Task.FromException(new IOException("injected enable failure")) : Task.CompletedTask; }
        public Task DisableAsync() { Disables++; return Task.CompletedTask; }
        public void Refresh() { }
        public void Dispose() { Disables++; }
    }
    internal static int Run(Application app, bool live)
    {
        var result = 1;
        app.Dispatcher.BeginInvoke(async () =>
        {
            MainWindow? window = null;
            try
            {
                var fake = new Backend();
                using (var controller = new TaskbarTransparency(fake))
                {
                    await Task.WhenAll(controller.SetEnabledAsync(true), controller.SetEnabledAsync(true));
                    if (!controller.Enabled || fake.Enables != 1) throw new Exception("Duplicate helper enabled.");
                    await controller.SetEnabledAsync(false);
                    fake.Fail = true;
                    try { await controller.SetEnabledAsync(true); throw new Exception("Failure not surfaced."); }
                    catch (IOException) { }
                    if (controller.Enabled || fake.Disables < 2) throw new Exception("Failed enable left taskbar owned.");
                }
                using (var pending = new TaskbarTransparency(new Backend { Slow = true }))
                {
                    var warm = pending.SetEnabledAsync(true); await Task.Delay(30);
                    var timer = System.Diagnostics.Stopwatch.StartNew(); pending.Dispose();
                    try { await warm; throw new Exception("Pending helper startup was not canceled."); } catch (OperationCanceledException) { }
                    if (timer.Elapsed.TotalSeconds > 2) throw new Exception("Exit waited for the full helper startup timeout.");
                }
                var directory = Path.Combine(Path.GetTempPath(), "TD-taskbar-check-" + Guid.NewGuid().ToString("N"));
                var state = new LibraryState(); var store = new StateStore(directory);
                window = new MainWindow(state, store, true); window.Show(); await Task.Delay(150);
                var box = (CheckBox)window.FindName("TransparentTaskbarBox");
                if (box.IsChecked == true || state.Settings.TransparentTaskbar) throw new Exception("Taskbar opt-in default changed.");
                if (!await window.SetTaskbarPreferenceAsync(true) || !store.Load().Settings.TransparentTaskbar || box.IsChecked != true) throw new Exception("Taskbar preference did not persist.");
                var persisted = store.Load().CreateSnapshot();
                if (!persisted.Settings.TransparentTaskbar) throw new Exception("Checkpoint loses taskbar preference.");
                if (!await window.SetTaskbarPreferenceAsync(false) || store.Load().Settings.TransparentTaskbar) throw new Exception("Disable did not persist.");
                window.Width = window.MinWidth; window.Height = window.MinHeight; window.UpdateLayout();
                var bounds = box.TransformToAncestor(window).TransformBounds(new Rect(box.RenderSize));
                if (!box.IsVisible || bounds.Right > window.ActualWidth || bounds.Bottom > window.ActualHeight || bounds.Left < 0) throw new Exception("Taskbar checkbox clips at minimum window size.");
                window.SaveRender(Path.Combine(directory, "taskbar-main-minimum.png"));
                Console.WriteLine("Taskbar UI render: " + Path.Combine(directory, "taskbar-main-minimum.png"));
                L.SetLanguage("en");
                if (L.Text("Taskbar trong suốt") != "Transparent taskbar") throw new Exception("Taskbar English missing.");
                window.Hide();
                Console.WriteLine("PASS taskbar preference: opt-in, main-screen checkbox, persistence/snapshot, disable, failure cleanup, duplicate-enable serialization");
                if (live)
                {
                    var backend = new TranslucentTaskbarBackend(directory);
                    using (var controller = new TaskbarTransparency(backend))
                    {
                        await controller.SetEnabledAsync(true);
                        var pid = backend.ProcessId;
                        if (pid == 0) throw new Exception("Native taskbar helper not running.");
                        Console.WriteLine("Taskbar transparent; owned helper PID=" + pid);
                        using (var other = new TaskbarTransparency(new TranslucentTaskbarBackend(directory + "-other")))
                        {
                            try { await other.SetEnabledAsync(true); throw new Exception("Second taskbar owner was accepted."); }
                            catch (IOException) { }
                            if (backend.ProcessId != pid) throw new Exception("Conflicting enable closed the first helper.");
                        }
                        await Task.Delay(8000);
                        await controller.SetEnabledAsync(false);
                        if (backend.ProcessId != 0) throw new Exception("Native taskbar helper survived disable.");
                    }
                    Console.WriteLine("PASS real Windows 11 helper start, hidden tray, transparent config and graceful restore/exit");
                }
                result = 0;
            }
            catch (Exception error) { Console.WriteLine("FAIL taskbar " + error); }
            finally { window?.Exit(); app.Shutdown(); Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        Dispatcher.Run(); return result;
    }
}
