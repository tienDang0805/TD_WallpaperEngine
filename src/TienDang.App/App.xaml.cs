using System.Security.Cryptography;
using System.Text;
using System.Windows.Threading;

namespace TienDang.App;

public partial class App : Application
{
    private Mutex? _instance;
    private StateStore? _store;
    public static string DataDirectory(string[] args)
    {
        var index = Array.IndexOf(args, "--data-dir");
        return index >= 0 && index + 1 < args.Length ? Path.GetFullPath(args[index + 1])
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TienDangWallpaper");
    }
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length >= 3 && e.Args[0] == "--wallpaper")
        {
            StaticFramePresentation.ConfigureWorker();
            var worker = new WallpaperWindow(e.Args[1], e.Args[2]);
            MainWindow = worker;
            worker.Show();
            return;
        }
        try
        {
            var directory = DataDirectory(e.Args);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(directory.ToUpperInvariant())))[..20];
            _instance = new Mutex(true, @"Local\TienDangWallpaper-" + hash, out var created);
            if (!created)
            {
                MessageBox.Show(L.Text("TD-WallpaperEngine đang chạy. Mở app từ biểu tượng ở khay hệ thống."), "TD-WallpaperEngine");
                Shutdown(); return;
            }
            // Acquire the single-instance mutex first. Do not read the library,
            // create a window, thumbnail service or decoder during logon settling.
            var delay = TienDang.App.Startup.LaunchDelay(e.Args);
            if (delay > TimeSpan.Zero) await Task.Delay(delay);
            _store = new(directory);
            var newLibrary = !File.Exists(_store.StatePath) && !File.Exists(_store.StatePath + ".bak");
            var state = _store.Load();
            var smoke = e.Args.Contains("--smoke-test");
            var window = new MainWindow(state, _store, smoke);
            if (newLibrary && (!smoke || e.Args.Contains("--starter-smoke"))) window.Loaded += async (_, _) =>
            { try { await window.AddStarterAsync(); } catch (Exception error) { _store.Log("Starter import: " + error); } };
            MainWindow = window;
            window.Show();
            if (e.Args.Contains("--minimized")) window.Hide();
            DispatcherUnhandledException += (_, eventArgs) =>
            {
                _store.Log(eventArgs.Exception.ToString());
                MessageBox.Show(eventArgs.Exception.Message, L.Text("TD-WallpaperEngine · Có lỗi"));
                eventArgs.Handled = true;
            };
            if (smoke)
            {
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    try { window.SaveRender(Path.Combine(directory, "ui-smoke.png")); File.WriteAllText(Path.Combine(directory, "smoke-ok.txt"), "UI rendered successfully."); }
                    catch (Exception ex) { File.WriteAllText(Path.Combine(directory, "smoke-error.txt"), ex.ToString()); Environment.ExitCode = 1; }
                    window.Exit();
                };
                timer.Start();
            }
        }
        catch (Exception ex)
        {
            _store?.Log(ex.ToString());
            MessageBox.Show(ex.Message, L.Text("Không mở được TD-WallpaperEngine"));
            Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        _instance?.Dispose();
        base.OnExit(e);
    }
}
