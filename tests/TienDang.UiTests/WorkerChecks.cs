using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using TienDang.App;

internal static class WorkerChecks
{
    internal static int Child(string pipe, string displayId, nint parent)
    {
        StaticFramePresentation.ConfigureWorker();
        var app = new Application();
        app.Run(new WallpaperWindow(pipe, displayId, parent)); return 0;
    }
    internal static ProcessStartInfo Start(string pipe, string display, nint parent)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("--native-worker"); start.ArgumentList.Add(pipe); start.ArgumentList.Add(display); start.ArgumentList.Add(parent.ToInt64().ToString()); return start;
    }
}