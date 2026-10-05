using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TienDang.App;

internal static class IpcChecks
{
    internal static async Task<int> Child(string pipeName, string mode)
    {
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(5000);
        using var reader = new StreamReader(pipe);
        using var writer = new StreamWriter(pipe) { AutoFlush = true };
        if (mode == "silent-ready") { await Task.Delay(20000); return 0; }
        await writer.WriteLineAsync(JsonSerializer.Serialize(new PlayerMessage { Command = "ready" }));
        while (await reader.ReadLineAsync() is {} line)
        {
            var message = JsonSerializer.Deserialize<PlayerMessage>(line)!;
            if (message.Command == "pause")
            {
                // Wrong correlation must not satisfy the outstanding request.
                await writer.WriteLineAsync(JsonSerializer.Serialize(new PlayerMessage { Command = "ack", RequestId = "wrong-id" }));
                continue;
            }
            if (message.Command == "load") { await Task.Delay(20000); continue; }
            await writer.WriteLineAsync(JsonSerializer.Serialize(new PlayerMessage { Command = "ack", RequestId = message.RequestId, Error = message.Command == "bad-command" ? "injected worker error" : null }));
            if (message.Command == "close") return 0;
        }
        return 0;
    }
    internal static async Task Run()
    {
        void Require(bool value, string name) { if (!value) throw new InvalidOperationException(name); Console.WriteLine("PASS IPC " + name); }
        ProcessStartInfo Start(string name, string mode)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
            start.ArgumentList.Add("--ipc-child"); start.ArgumentList.Add(name); start.ArgumentList.Add(mode); return start;
        }
        var callbacks = 0;
        var host = await PlayerHost.Start("no-desktop", _ => Interlocked.Increment(ref callbacks), default, name => Start(name, "normal"));
        var pid = host.ProcessId;
        try
        {
            await host.Send(new() { Command = "options" });
            Require(host.PendingCommands == 0, "successful acknowledgement removes correlation");
            using (var cancel = new CancellationTokenSource(200))
            {
                var watch = Stopwatch.StartNew();
                try { await host.Send(new() { Command = "pause" }, cancel.Token); throw new Exception("Missing cancellation"); }
                catch (OperationCanceledException) { }
                Require(watch.ElapsedMilliseconds < 1000 && host.PendingCommands == 0, "wrong acknowledgement is ignored; cancellation clears pending request promptly");
            }
            await host.Send(new() { Command = "play" }); Require(host.PendingCommands == 0, "cancelled command does not poison next command");
            try { await host.Send(new() { Command = "bad-command" }); throw new Exception("Missing worker error"); }
            catch (IOException ex) { Require(ex.Message == "injected worker error", "worker command failure reaches caller"); }
            var pending = host.Send(new() { Command = "load" });
            await Task.Delay(100); host.Dispose();
            try { await pending; throw new Exception("Missing dispose cancellation"); } catch (OperationCanceledException) { } catch (IOException) { }
            await host.ExitCompletion;
            Require(!Alive(pid) && host.PendingCommands == 0, "dispose cancels outstanding acknowledgement and confirms owned worker exit");
        }
        finally { host.Dispose(); await host.ExitCompletion; }
        var clock = Stopwatch.StartNew();
        using var startCancel = new CancellationTokenSource(350);
        try { await PlayerHost.Start("no-desktop", _ => {}, startCancel.Token, name => Start(name, "silent-ready")); throw new Exception("Missing startup cancellation"); }
        catch (OperationCanceledException) { }
        Require(clock.ElapsedMilliseconds < 1500, "cancelled handshake returns without blocking UI for worker cleanup");
        // Native command budget itself is exercised without caller cancellation.
        var deadlineHost = await PlayerHost.Start("no-desktop", _ => {}, default, name => Start(name, "normal"));
        try
        {
            clock.Restart();
            try { await deadlineHost.Send(new() { Command = "pause" }); throw new Exception("Missing command deadline"); } catch (OperationCanceledException) { }
            Require(clock.ElapsedMilliseconds >= 9500 && clock.ElapsedMilliseconds < 12000 && deadlineHost.PendingCommands == 0, "unresponsive worker acknowledgement expires at ten-second budget");
        }
        finally { deadlineHost.Dispose(); await deadlineHost.ExitCompletion; }
        Console.WriteLine("PASS IPC suite");
    }
    private static bool Alive(int pid) { try { using var p = Process.GetProcessById(pid); return !p.HasExited; } catch (ArgumentException) { return false; } }
}