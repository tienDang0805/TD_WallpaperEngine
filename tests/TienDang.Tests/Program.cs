using System.Text;
using System.Text.Json;
using TienDang.Core;

if (args.Length > 0 && args[0] == "--process-fixture") return YouTubeChecks.ProcessFixture(args).GetAwaiter().GetResult();

var tests = new List<(string Name, Action Run)>();
void Test(string name, Action run) => tests.Add((name, run));
void Assert(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
Test("Phase four library, backup, ownership and profile regression", PhaseFourChecks.Run);
var items = Enumerable.Range(0, 7).Select(i => new WallpaperItem { Id = i.ToString(), Name = $"Wallpaper {i}", Path = $"test-{i}.png" }).ToList();

Test("Starting shuffle honors selection and exhausts every wallpaper before repeats", () =>
{
    for (var seed = 0; seed < 100; seed++)
    {
        var rotation = new Rotation(new Random(seed));
        var preferred = items[seed % items.Count];
        Assert(rotation.Start(items, true, preferred.Id)?.Id == preferred.Id);
        var seen = new HashSet<string> { preferred.Id };
        for (var n = 1; n < items.Count; n++) Assert(seen.Add(rotation.Next(items, true)!.Id), "Selected first item repeated inside round");
        var last = rotation.CurrentId;
        Assert(rotation.Next(items, true)!.Id != last, "Adjacent repeat across round boundary");
        rotation.Next(items, true); // Restart must discard the prior bag/history.
        Assert(rotation.Start(items, true, preferred.Id)?.Id == preferred.Id);
        seen = [preferred.Id];
        for (var n = 1; n < items.Count; n++) Assert(seen.Add(rotation.Next(items, true)!.Id));
    }
});
Test("Starting selected wallpaper supports sequential, empty and missing selections", () =>
{
    var rotation = new Rotation(new Random(42));
    Assert(rotation.Start(items, false, "4")?.Id == "4");
    Assert(rotation.Next(items, false)?.Id == "5");
    Assert(rotation.Previous(items)?.Id == "4");
    Assert(rotation.Next(items, false)?.Id == "5");
    Assert(rotation.Start([], true, "gone") == null);
    Assert(rotation.Start(items, true, "gone") is { } first && items.Contains(first));
    Assert(rotation.Start(items.Take(1).ToArray(), true, "0")?.Id == "0");
    Assert(rotation.Next(items.Take(1).ToArray(), true)?.Id == "0");
});
Test("Language and FPS are additive settings with safe defaults and normalization", () =>
{
    var legacy = JsonSerializer.Deserialize<AppSettings>("{}")!;
    Assert(legacy.Language == "vi" && legacy.FrameRateLimit == 0);
    var state = new LibraryState();
    foreach (var fps in new[] { 0, 15, 30, 60 })
    {
        state.Settings.Language = "en"; state.Settings.FrameRateLimit = fps; state.Normalize();
        Assert(state.Settings.Language == "en" && state.Settings.FrameRateLimit == fps);
        var copy = JsonSerializer.Deserialize<LibraryState>(JsonSerializer.Serialize(state))!;
        Assert(copy.Settings.Language == "en" && copy.Settings.FrameRateLimit == fps);
    }
    state.Settings.Language = "invalid"; state.Settings.FrameRateLimit = -1; state.Normalize();
    Assert(state.Settings.Language == "vi" && state.Settings.FrameRateLimit == 0);
});
Test("Sequential rotation follows playlist order and wraps", () =>
{
    var rotation = new Rotation();
    for (var i = 0; i < 30; i++) Assert(rotation.Next(items, false)!.Id == items[i % items.Count].Id);
});
Test("Shuffle has no repeats in each round and no repeat across boundary", () =>
{
    for (var seed = 0; seed < 50; seed++)
    {
        var rotation = new Rotation(new Random(seed));
        string? last = null;
        for (var round = 0; round < 20; round++)
        {
            var ids = new HashSet<string>();
            for (var n = 0; n < items.Count; n++)
            {
                var id = rotation.Next(items, true)!.Id;
                Assert(id != last, "Repeated adjacent item");
                Assert(ids.Add(id), "Repeated item inside shuffle round");
                last = id;
            }
            Assert(ids.Count == items.Count);
        }
    }
});
Test("Empty, single and changing playlists remain valid", () =>
{
    var rotation = new Rotation(new Random(1));
    Assert(rotation.Next([], true) == null);
    Assert(rotation.Next(items.Take(1).ToArray(), true)!.Id == "0");
    for (var i = 0; i < 5; i++) Assert(rotation.Next(items.Take(1).ToArray(), true)!.Id == "0");
    for (var i = 0; i < 20; i++) { var id = rotation.Next(items.Skip(4).ToArray(), true)!.Id; Assert(items.Skip(4).Any(x => x.Id == id)); }
});
Test("Previous and next use actual playback history", () =>
{
    var rotation = new Rotation(new Random(2));
    var a = rotation.Next(items, true)!.Id;
    var b = rotation.Next(items, true)!.Id;
    var c = rotation.Next(items, true)!.Id;
    Assert(rotation.Previous(items)!.Id == b);
    Assert(rotation.Previous(items)!.Id == a);
    Assert(rotation.Next(items, true)!.Id == b);
    Assert(rotation.Next(items, true)!.Id == c);
});
Test("Daytime schedule excludes end boundary and wrong monitor", () =>
{
    var r = new ScheduleRule { MonitorId = "screen1", Days = [DayOfWeek.Monday] };
    var monday = new DateTime(2026, 10, 5);
    Assert(!r.Matches(monday.AddHours(6), "screen1"));
    Assert(r.Matches(monday.AddHours(7), "screen1"));
    Assert(!r.Matches(monday.AddHours(18), "screen1"));
    Assert(!r.Matches(monday.AddHours(8), "screen2"));
    Assert(!r.Matches(monday.AddDays(1).AddHours(8), "screen1"));
});
Test("Overnight schedule belongs to starting weekday", () =>
{
    var r = new ScheduleRule { Start = TimeSpan.FromHours(22), End = TimeSpan.FromHours(7), Days = [DayOfWeek.Monday] };
    var monday = new DateTime(2026, 10, 5);
    Assert(!r.Matches(monday.AddHours(6), "screen1"));
    Assert(r.Matches(monday.AddHours(23), "screen1"));
    Assert(r.Matches(monday.AddDays(1).AddHours(6), "screen1"));
    Assert(!r.Matches(monday.AddDays(1).AddHours(7), "screen1"));
    Assert(!r.Matches(monday.AddDays(1).AddHours(23), "screen1"));
});
Test("All-day, disabled and monitor-specific rule priorities", () =>
{
    var state = new LibraryState();
    state.Playlists.Add(new() { Id = "night" });
    state.Playlists.Add(new() { Id = "work" });
    state.Settings.Schedules.Add(new() { Start = TimeSpan.Zero, End = TimeSpan.Zero, PlaylistId = "night" });
    state.Settings.Schedules.Add(new() { Start = TimeSpan.Zero, End = TimeSpan.Zero, PlaylistId = "work", MonitorId = "screen1" });
    Assert(state.ResolvePlaylist(DateTime.Now, new() { MonitorId = "screen1" }) == "work");
    Assert(state.ResolvePlaylist(DateTime.Now, new() { MonitorId = "screen2" }) == "night");
    state.Settings.Schedules[1].Enabled = false;
    Assert(state.ResolvePlaylist(DateTime.Now, new() { MonitorId = "screen1" }) == "night");
});
Test("Paused timer preserves remaining duration and ignores negative delta", () =>
{
    var clock = new PlaybackClock();
    clock.Reset(10);
    Assert(!clock.Advance(4, false) && clock.RemainingSeconds == 6);
    Assert(!clock.Advance(100, true) && clock.RemainingSeconds == 6);
    Assert(!clock.Advance(-100, false) && clock.RemainingSeconds == 6);
    Assert(clock.Advance(6, false) && clock.RemainingSeconds == 0);
});
Test("Normalize removes orphan membership and clamps settings", () =>
{
    var state = new LibraryState { Items = [items[0], items[0]], Playlists = [new() { Id = "custom", ItemIds = ["0", "gone", "0"] }] };
    state.Settings.IntervalSeconds = -1;
    state.Settings.Volume = 1000;
    state.Normalize();
    Assert(state.Items.Count == 1 && state.GetItems("custom").Count == 1);
    Assert(state.Playlists.Any(p => p.Id == Playlist.AllId));
    Assert(state.Settings.IntervalSeconds == 5 && state.Settings.Volume == 100);
});
Test("State round-trip, backup recovery and damaged file preservation", () =>
{
    var directory = Path.Combine(Path.GetTempPath(), "TienDang-tests-" + Guid.NewGuid().ToString("N"));
    var store = new StateStore(directory);
    var state = new LibraryState { Items = items };
    state.Settings.Schedules.Add(new() { Start = TimeSpan.FromHours(22), End = TimeSpan.FromHours(7) });
    store.Save(state);
    state.Settings.IntervalSeconds = 42;
    store.Save(state);
    Assert(store.Load().Settings.IntervalSeconds == 42);
    Assert(store.Load().Settings.Schedules[0].Start == TimeSpan.FromHours(22));
    File.WriteAllText(store.StatePath, "{corrupted");
    var recovered = store.Load();
    Assert(recovered.Items.Count == items.Count);
    Assert(recovered.Settings.IntervalSeconds == 600, "Should use previous backup");
    Assert(store.RecoveryNotice != null);
    Assert(Directory.GetFiles(directory, "library-damaged-*").Length == 1);
    // Preserve the fixture folder for investigating failures; it is in the OS temp directory.
});
Test("File classification ignores extension case", () =>
{
    Assert(MediaTypes.Supported("Photo.JPEG") && MediaTypes.IsVideo("Clip.MP4"));
    Assert(MediaTypes.Supported("Motion.webm") && MediaTypes.IsVideo("Motion.WeBm"));
    Assert(MediaTypes.FileDialogFilter.Contains("*.webm", StringComparison.OrdinalIgnoreCase));
    Assert(!MediaTypes.Supported("script.exe") && !MediaTypes.IsVideo("photo.png"));
});

Test("Manual pin cannot repeat immediately from an existing shuffle bag", () =>
{
    var rotation = new Rotation(new Random(9));
    rotation.Next(items, true);
    for (var n = 0; n < 25; n++)
    {
        var pinned = items[n % items.Count];
        rotation.SetCurrent(pinned.Id);
        Assert(rotation.Next(items, true)!.Id != pinned.Id);
    }
});
Test("State saving supports repeated overwrites without leftover temp files", () =>
{
    var directory = Path.Combine(Path.GetTempPath(), "TienDang-overwrite-" + Guid.NewGuid().ToString("N"));
    var store = new StateStore(directory);
    var state = new LibraryState();
    for (var i = 0; i < 30; i++)
    {
        state.Settings.IntervalSeconds = i + 5;
        store.Save(state);
        Assert(store.Load().Settings.IntervalSeconds == i + 5);
    }
    Assert(Directory.GetFiles(directory, "*.tmp").Length == 0);
});
Test("Transactional URL downloads and failure cleanup", () => DownloadChecks.Run().GetAwaiter().GetResult());
Test("YouTube routing, isolated download and process safety", () => YouTubeChecks.Run().GetAwaiter().GetResult());
Test("Null and nested schema recovery", PersistenceChecks.NullSchemaRecovery);
Test("Good backup survives recovery saves and external corruption", PersistenceChecks.BackupAfterRecovery);
Test("Persistence failures retain data and clean temporary files", PersistenceChecks.FailureCleanup);
Test("Detached snapshots, stale checkpoint guard and serialized concurrent commits", SnapshotChecks.Run);
var failures = 0;
foreach (var test in tests)
{
    try { test.Run(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception ex) { failures++; Console.WriteLine("FAIL " + test.Name + ": " + ex.Message); }
}
Console.WriteLine($"{tests.Count - failures}/{tests.Count} checks passed.");

if (args.Length >= 2 && args[0] == "--fixtures")
{
    var folder = Path.GetFullPath(args[1]);
    Directory.CreateDirectory(folder);
    var media = Path.Combine(folder, "fixtures");
    Directory.CreateDirectory(media);
    for (var i = 0; i < 3; i++) File.WriteAllBytes(Path.Combine(media, $"Landscape-{i + 1}.bmp"), Fixture.Bitmap(i));
    File.WriteAllBytes(Path.Combine(media, "Motion-test.avi"), Fixture.Avi());
    var state = new LibraryState
    {
        Items = Directory.GetFiles(media).Select(path => new WallpaperItem { Name = Path.GetFileNameWithoutExtension(path), Path = path, OriginalPath = path, IsVideo = MediaTypes.IsVideo(path) }).ToList()
    };
    state.Settings.IntervalSeconds = 5;
    state.Settings.Shuffle = false;
    state.Settings.ResumeOnLaunch = false;
    state.Settings.PauseFullscreen = false;
    state.Settings.AdvanceAtVideoEnd = true;
    new StateStore(folder).Save(state);
    Console.WriteLine("Fixtures: " + folder);
}
return failures == 0 ? 0 : 1;

static class Fixture
{
    private const int Width = 320, Height = 180;
    private static byte[] Pixels(int frame)
    {
        var pixels = new byte[Width * Height * 3];
        for (var y = 0; y < Height; y++) for (var x = 0; x < Width; x++)
        {
            var offset = (y * Width + x) * 3;
            pixels[offset] = (byte)(70 + x * 130 / Width);
            pixels[offset + 1] = (byte)(60 + y * 140 / Height);
            pixels[offset + 2] = (byte)(35 + ((x + frame * 15) % Width) * 100 / Width);
            if (Math.Abs(x - (80 + frame * 8) % Width) < 20 && Math.Abs(y - 100) < 20)
            { pixels[offset] = 160; pixels[offset + 1] = 240; pixels[offset + 2] = 120; }
        }
        return pixels;
    }
    private static byte[] Bytes(Action<BinaryWriter> write)
    {
        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory, Encoding.ASCII, true);
        write(writer); writer.Flush(); return memory.ToArray();
    }
    private static void Four(BinaryWriter w, string value) => w.Write(Encoding.ASCII.GetBytes(value));
    private static byte[] Format() => Bytes(w =>
    {
        w.Write(40); w.Write(Width); w.Write(Height); w.Write((short)1); w.Write((short)24); w.Write(0);
        w.Write(Width * Height * 3); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
    });
    public static byte[] Bitmap(int frame) => Bytes(w =>
    {
        Four(w, "BM"); w.Write(54 + Width * Height * 3); w.Write(0); w.Write(54); w.Write(Format()); w.Write(Pixels(frame));
    });
    private static byte[] Chunk(string type, byte[] data) => Bytes(w =>
    {
        Four(w, type); w.Write(data.Length); w.Write(data); if ((data.Length & 1) != 0) w.Write((byte)0);
    });
    private static byte[] List(string type, params byte[][] chunks) => Chunk("LIST", Bytes(w =>
    {
        Four(w, type); foreach (var c in chunks) w.Write(c);
    }));
    public static byte[] Avi()
    {
        const int count = 20, rate = 10;
        var avih = Chunk("avih", Bytes(w =>
        {
            w.Write(1_000_000 / rate); w.Write(Width * Height * 3 * rate); w.Write(0); w.Write(0x10); w.Write(count);
            w.Write(0); w.Write(1); w.Write(Width * Height * 3); w.Write(Width); w.Write(Height); for (var i = 0; i < 4; i++) w.Write(0);
        }));
        var strh = Chunk("strh", Bytes(w =>
        {
            Four(w, "vids"); Four(w, "DIB "); w.Write(0); w.Write((short)0); w.Write((short)0); w.Write(0);
            w.Write(1); w.Write(rate); w.Write(0); w.Write(count); w.Write(Width * Height * 3); w.Write(-1); w.Write(0);
            w.Write((short)0); w.Write((short)0); w.Write((short)Width); w.Write((short)Height);
        }));
        var header = List("hdrl", avih, List("strl", strh, Chunk("strf", Format())));
        var frames = Enumerable.Range(0, count).Select(i => Chunk("00db", Pixels(i))).ToArray();
        var movi = List("movi", frames);
        var index = Chunk("idx1", Bytes(w =>
        {
            var offset = 4;
            foreach (var frame in frames) { Four(w, "00db"); w.Write(0x10); w.Write(offset); w.Write(Width * Height * 3); offset += frame.Length; }
        }));
        return Chunk("RIFF", Bytes(w => { Four(w, "AVI "); w.Write(header); w.Write(movi); w.Write(index); }));
    }
}