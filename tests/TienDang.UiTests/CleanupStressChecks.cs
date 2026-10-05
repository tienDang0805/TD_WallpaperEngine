using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using TienDang.App;

internal static class CleanupStressChecks
{
    internal static int Run(Application app, string root, string video, int cycles = 48, int dwellMs = 550, string? reportPath = null)
    {
        var result = 1; var window = new Window { Title = "TienDang cleanup stress", Width = 720, Height = 450, ShowActivated = false };
        var presenter = new WallpaperPresenter(window);
        window.Loaded += async (_, _) =>
        {
            try { await Check(window, presenter, root, video, cycles, dwellMs, reportPath); result = 0; }
            catch (Exception ex) { Console.WriteLine("FAIL cleanup stress " + ex); }
            finally { presenter.Dispose(); window.Close(); app.Shutdown(); Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        };
        window.Show(); Dispatcher.Run(); return result;
    }
    private sealed record Sample(int Cycle, double AppPrivateMiB, int AppHandles, double DecoderPrivateMiB, int DecoderHandles, int Pid, double LoadMs);
    private static async Task Check(Window window, WallpaperPresenter presenter, string root, string video, int cycles = 48, int dwellMs = 550, string? reportPath = null)
    {
        void Require(bool value, string name) { if (!value) throw new InvalidOperationException(name); Console.WriteLine("PASS cleanup stress " + name); }
        var samples = new List<Sample>(); var pids = new HashSet<int>(); var bridges = 0; var maxBridgePixels = 0;
        presenter.Failed += (generation, error) => Console.WriteLine("Media failed " + generation + ": " + error);
        presenter.TransitionFailed += (generation, error) => Console.WriteLine("Transition failed " + generation + ": " + error);
        presenter.Crashed += (generation, error) => Console.WriteLine("Decoder crashed " + generation + ": " + error);
        var parent = new WindowInteropHelper(window).Handle;
        using var self = Process.GetCurrentProcess();
        var handleTypes = new Dictionary<int, Dictionary<string, int>>();
        var diagnostics = new List<PlayerDiagnostics>();
        for (var i = 1; i <= cycles; i++)
        {
            var oldPid = presenter.ActiveProcessId;
            if (oldPid > 0) pids.Add(oldPid);
            var timer = Stopwatch.StartNew();
            var load = presenter.LoadAsync(new() { Command = "load", Generation = i, Path = video, IsVideo = true, Muted = true, Fit = i % 3 == 0 ? "Fit" : i % 3 == 1 ? "Fill" : "Stretch", FrameRateLimit = 0 });
            while (!load.IsCompleted)
            {
                if (timer.Elapsed.TotalSeconds > 30) throw new TimeoutException("stress load");
                if (presenter.ActiveHandle != 0 && NativeDesktop.GetWindow(parent, 5) != presenter.ActiveHandle) throw new InvalidOperationException("Exposed pending/default surface");
                if (presenter.BridgePixelCount > 0)
                {
                    bridges++; maxBridgePixels = Math.Max(maxBridgePixels, presenter.BridgePixelCount);
                    if (presenter.BridgePixelCount > 2_073_600) throw new InvalidOperationException("Static frame exceeds pixel budget");
                }
                if (oldPid > 0 && presenter.PendingProcessId > 0 && Alive(oldPid)) throw new InvalidOperationException("Old/new decoder overlap");
                await Task.Delay(25);
            }
            await load;
            if (presenter.VideoSlotCount > 2) throw new InvalidOperationException("Unbounded video surface pool");
            if (presenter.ActiveGeneration != i || presenter.ActiveProcessId == 0) throw new InvalidOperationException("Load did not commit");
            if (oldPid > 0 && Alive(oldPid)) throw new InvalidOperationException("Old decoder alive after replacement");
            var marker = Path.Combine(root, "artifacts", "stage3-cleanup-active.json");
            File.WriteAllText(marker + ".tmp", JsonSerializer.Serialize(new { Pid = presenter.ActiveProcessId, Fixture = "cleanup-cycle-" + i, Limit = 0 }));
            File.Move(marker + ".tmp", marker, true);
            await Task.Delay(dwellMs); self.Refresh();
            using var decoder = Process.GetProcessById(presenter.ActiveProcessId); decoder.Refresh();
            samples.Add(new(i, self.PrivateMemorySize64 / 1048576d, self.HandleCount, decoder.PrivateMemorySize64 / 1048576d, decoder.HandleCount, decoder.Id, timer.Elapsed.TotalMilliseconds));
            if (i is 8 or 16 or 24 || i == cycles)
            {
                var snapshot = (await presenter.CaptureDiagnosticsAsync()).ActiveVideo!;
                diagnostics.Add(snapshot);
                Require(snapshot.VideoOutput == "gpu" && snapshot.HwdecCurrent is "d3d11va" or "d3d11va-copy", "worker still-image composition leaves 4K video hardware decoding on GPU cycle " + i);
            }
            if (i is 8 or 16 or 24 || i == cycles) { handleTypes[i] = HandleProbe.Capture(true); Console.WriteLine("Handle types " + i + ": " + JsonSerializer.Serialize(handleTypes[i])); }
            if (i % 8 == 0) Console.WriteLine($"Stress cycle {i}/{cycles} appRAM={samples.Last().AppPrivateMiB:F1}MiB handles={self.HandleCount} decoderRAM={samples.Last().DecoderPrivateMiB:F1}MiB");
        }
        var active = presenter.ActiveProcessId; pids.Add(active); presenter.Dispose(); await Task.Delay(300);
        Require(pids.All(pid => !Alive(pid)), "every observed owned decoder exits");
        Require(presenter.CleanupEvidence.Count == Math.Min(cycles - 1, 64) && presenter.CleanupEvidence.All(e => e.OldExitTicks < e.NewStartTicks), "retained handoffs record exit before next start");
        Require(bridges > 0 && maxBridgePixels <= 2_073_600, "4K source produces bounded static bridge");
        var initial = samples.Where(s => s.Cycle >= 9 && s.Cycle <= 16).ToArray(); var final = samples.Where(s => s.Cycle >= cycles - 7).ToArray();
        double Median(IEnumerable<double> values) { var sorted = values.Order().ToArray(); return (sorted[(sorted.Length - 1) / 2] + sorted[sorted.Length / 2]) / 2; }
        var ramGrowth = Median(final.Select(s => s.AppPrivateMiB)) - Median(initial.Select(s => s.AppPrivateMiB));
        var handleGrowth = Median(final.Select(s => (double)s.AppHandles)) - Median(initial.Select(s => (double)s.AppHandles));
        File.WriteAllText(reportPath ?? Path.Combine(root, "artifacts", "stage3-cleanup-stress.json"), JsonSerializer.Serialize(new { Samples = samples, HandleTypes = handleTypes, Diagnostics = diagnostics, Bridges = bridges, MaxBridgePixels = maxBridgePixels, AppRamMedianGrowthMiB = ramGrowth, AppHandleMedianGrowth = handleGrowth, Fixture = video, Cycles = cycles, DwellMs = dwellMs, Scope = dwellMs < 600_000 ? "Accelerated short regression in ordinary 720x450 window; not 8/24h soak" : "Timed native presenter soak in ordinary 720x450 window; does not cover real boot/sleep/dual physical displays", Evidence = presenter.CleanupEvidence.Select(e => new { e.Generation, e.OldPid, e.OldExitTicks, e.NewPid, e.NewStartTicks }) }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Stress warm-to-end RAM median delta={ramGrowth:F2}MiB handles={handleGrowth:F0}");
        Require(handleGrowth <= 20 && ramGrowth <= 128, "short-run handle/RAM growth stays within regression ceiling");
        Console.WriteLine("PASS cleanup stress suite");
    }
    private static bool Alive(int pid) { try { using var p = Process.GetProcessById(pid); return !p.HasExited; } catch (ArgumentException) { return false; } }
}