using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using TD_Wallpaper.App;

internal static class HardwareMatrixChecks
{
    private sealed record Fixture
    {
        public string Name { get; init; } = "";
        public string Codec { get; init; } = "";
        public string Path { get; init; } = "";
        public int Width { get; init; }
        public int Height { get; init; }
        public double FPS { get; init; }
        public string ExpectedMode { get; init; } = "hardware";
    }
    private sealed record Result
    {
        public string Fixture { get; init; } = "";
        public int Limit { get; init; }
        public PlayerDiagnostics? Sample { get; init; }
        public double TimelineDeltaSeconds { get; init; }
        public double WallSeconds { get; init; }
        public long? DroppedFrameDelta { get; init; }
        public bool ProcessExited { get; init; }
        public List<string> Failures { get; init; } = [];
    }
    internal static int Run(string fixtureFile, string output)
    {
        var application = new Application();
        var surface = new MpvVideoSurface();
        var window = new Window { Title = "TD_Wallpaper · Hardware matrix fixture", Width = 720, Height = 450, Content = surface, ShowActivated = false };
        var result = 1;
        window.Loaded += async (_, _) =>
        {
            try { result = await Check(surface.Handle, fixtureFile, output); }
            catch (Exception ex) { Console.WriteLine("FAIL matrix " + ex); }
            finally { window.Close(); }
        };
        application.Run(window); return result;
    }
    private static async Task<int> Check(nint handle, string fixtureFile, string output)
    {
        var fixtures = JsonSerializer.Deserialize<List<Fixture>>(File.ReadAllText(fixtureFile), new JsonSerializerOptions { PropertyNameCaseInsensitive=true })!;
        var results = new List<Result>();
        var marker = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(output)!, "stage3-hardware-active.json");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(output)!);
        void Write(string path, object value)
        {
            var temp=path+".tmp"; File.WriteAllText(temp,JsonSerializer.Serialize(value,new JsonSerializerOptions {WriteIndented=true})); File.Move(temp,path,true);
        }
        foreach (var fixture in fixtures)
        foreach (var limit in new[] {0,15,30,60})
        {
            var failures = new List<string>(); var pid=0; PlayerDiagnostics? sample=null; var delta=0d; var elapsed=0d; long? dropped=null;
            using var player = new MpvPlayer(handle);
            try
            {
                var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                player.FrameReady += () => ready.TrySetResult(); player.Failed += message => ready.TrySetException(new IOException(message));
                await player.StartAsync(fixture.Path,true,0,"Fit",true,limit);
                pid=player.ProcessId;
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(25));
                Write(marker,new {Pid=pid,Fixture=fixture.Name,Limit=limit,StartedUtc=DateTime.UtcNow});
                await player.SetPausedAsync(false); await Task.Delay(900);
                var before=await player.CaptureDiagnosticsAsync();
                var position=(await player.GetPropertyAsync("time-pos")).GetDouble();
                var clock=Stopwatch.StartNew(); await Task.Delay(2500);
                delta=(await player.GetPropertyAsync("time-pos")).GetDouble()-position; elapsed=clock.Elapsed.TotalSeconds;
                sample=await player.CaptureDiagnosticsAsync();
                dropped=sample.OutputDroppedFrames-before.OutputDroppedFrames;
                if(sample.ProcessId!=pid || sample.VideoOutput!="gpu") failures.Add("Native owned PID / video output missing");
                if(sample.Width!=fixture.Width || sample.Height!=fixture.Height) failures.Add("Decoded resolution mismatch");
                if(sample.SourceFps is not {} source || Math.Abs(source-fixture.FPS)>.5) failures.Add("Source FPS mismatch");
                var expected=limit==0?fixture.FPS:Math.Min(fixture.FPS,limit);
                if(sample.OutputFps is not {} fps || Math.Abs(fps-expected)>1) failures.Add("Output FPS mismatch");
                if(sample.DecodeMode!=fixture.ExpectedMode) failures.Add("Expected "+fixture.ExpectedMode+", observed "+sample.DecodeMode);
                if(sample.RequestedHwdec!=(limit==0?"auto":"auto-copy")) failures.Add("Requested hardware mode mismatch");
                if(sample.DecodeMode=="software" && !sample.SoftwareFallback) failures.Add("Software fallback was not detected");
                if(sample.PrivateMiB is null || sample.CpuPercentAllCores is null || sample.CpuSampleSeconds<2) failures.Add("Process resource sample unavailable");
                if(Math.Abs(delta-elapsed)>.35) failures.Add("Timeline differs from wall clock by >350ms");
                if(player.PendingCommandCount!=0) failures.Add("Pending IPC leak");
            }
            catch(Exception ex){failures.Add(ex.ToString());}
            finally {Write(marker,new {Pid=0,Fixture=fixture.Name,Limit=limit});player.Dispose();}
            var exited=pid==0; var exitClock=Stopwatch.StartNew();
            while(!exited && exitClock.Elapsed.TotalSeconds<2)
            {
                try {using var process=Process.GetProcessById(pid);exited=process.HasExited;}catch(ArgumentException){exited=true;}
                if(!exited) await Task.Delay(25);
            }
            if(!exited) failures.Add("Owned decoder did not exit");
            var row = new Result {Fixture=fixture.Name,Limit=limit,Sample=sample,TimelineDeltaSeconds=delta,WallSeconds=elapsed,DroppedFrameDelta=dropped,ProcessExited=exited,Failures=failures};results.Add(row);
            Write(output,new {Window="720x450 native D3D11; decoded source resolution preserved; 900ms settle + 2.5s CPU interval per fresh process",LogicalCores=Environment.ProcessorCount,Results=results});
            Console.WriteLine((failures.Count==0?"PASS":"FAIL")+$" matrix {fixture.Name} cap={limit} hwdec={sample?.HwdecCurrent} CPU={sample?.CpuPercentAllCores:0.00}% RAM={sample?.PrivateMiB:0.0}MiB dropped={dropped} "+string.Join("; ",failures));
        }
        Console.WriteLine($"Matrix: {results.Count(r=>r.Failures.Count==0)}/{results.Count} cases passed.");
        return results.Any(r=>r.Failures.Count>0)?1:0;
    }
}