using System.Text;
using System.Text.Json;

namespace TD_Wallpaper.Core;

public sealed class StateStore(string directory)
{
    public string DirectoryPath { get; } = Path.GetFullPath(directory);
    public string StatePath => Path.Combine(DirectoryPath, "library.json");
    public string? RecoveryNotice { get; private set; }
    private readonly object _saveGate = new();
    private long _revision;
    public long Revision => Interlocked.Read(ref _revision);
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public LibraryState Load()
    {
        Directory.CreateDirectory(DirectoryPath);
        RecoveryNotice = null;
        if (!File.Exists(StatePath))
        {
            if (!File.Exists(StatePath + ".bak")) return new();
            try
            {
                var recovered = Read(StatePath + ".bak");
                RecoveryNotice = "Không tìm thấy library.json. App đã khôi phục bản sao lưu.";
                return recovered;
            }
            catch (Exception ex) when (IsRecoveryError(ex))
            {
                RecoveryNotice = "Không đọc được bản sao lưu. File cũ được giữ lại; app mở thư viện trống.";
                return new();
            }
        }
        try { return Read(StatePath); }
        catch (Exception ex) when (IsRecoveryError(ex))
        {
            var preserved = PreserveDamagedFile();
            LibraryState state;
            var restored = false;
            try { state = Read(StatePath + ".bak"); restored = true; }
            catch (Exception backupError) when (IsRecoveryError(backupError)) { state = new(); }
            RecoveryNotice = (preserved ? "Dữ liệu bị lỗi đã được giữ lại. " : "Chưa sao chép được file lỗi; file gốc vẫn được giữ. ") +
                (restored ? "App đã khôi phục bản sao lưu." : "Không có backup hợp lệ; app mở thư viện trống.");
            return state;
        }
    }

    private static bool IsRecoveryError(Exception error) => error is JsonException or IOException or InvalidOperationException;
    private static LibraryState Parse(string json)
    {
        var state = JsonSerializer.Deserialize<LibraryState>(json, Options) ?? throw new JsonException("Empty state");
        if (state.Version != 1) throw new JsonException("Unsupported state version");
        state.Normalize();
        return state;
    }
    private static LibraryState Read(string path) => Parse(File.ReadAllText(path));
    private bool PreserveDamagedFile()
    {
        try
        {
            File.Copy(StatePath, Path.Combine(DirectoryPath, $"library-damaged-{DateTime.Now:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.json"), false);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log("Cannot preserve damaged library: " + ex.Message);
            return false;
        }
    }

    private byte[]? ValidPrimarySnapshot()
    {
        if (!File.Exists(StatePath)) return null;
        var bytes = File.ReadAllBytes(StatePath);
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        try { Parse(reader.ReadToEnd()); return bytes; }
        catch (JsonException) { return null; } // A damaged primary must never replace a good backup.
    }

    private static void WriteDurable(string path, byte[] bytes)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes); stream.Flush(true);
    }
    public void Save(LibraryState state)
    {
        lock (_saveGate) { SaveCore(state); Interlocked.Increment(ref _revision); }
    }
    public bool TrySaveSnapshot(LibraryState snapshot, long expectedRevision)
    {
        lock (_saveGate)
        {
            if (_revision != expectedRevision) return false;
            SaveCore(snapshot); Interlocked.Increment(ref _revision); return true;
        }
    }
    private void SaveCore(LibraryState state)
    {
        state.ValidateStructure();
        if (state.Version != 1) throw new JsonException("Unsupported state version");
        // Serialize before touching either committed file.
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(state, Options));
        Directory.CreateDirectory(DirectoryPath);
        var temp = StatePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var backupTemp = StatePath + ".bak." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            WriteDurable(temp, bytes);
            var previous = ValidPrimarySnapshot();
            if (previous != null)
            {
                WriteDurable(backupTemp, previous);
                File.Move(backupTemp, StatePath + ".bak", true);
            }
            // For a missing/damaged primary, preserve any existing valid backup unchanged.
            File.Move(temp, StatePath, true);
        }
        finally
        {
            DeleteOwnedTemporary(temp);
            DeleteOwnedTemporary(backupTemp);
        }
    }
    private void DeleteOwnedTemporary(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Log("Cannot remove owned library temporary file: " + ex.Message); }
    }

    public void Log(string message)
    {
        try
        {
            var path = Path.Combine(DirectoryPath, "app.log");
            if (File.Exists(path) && new FileInfo(path).Length > 2_000_000) File.Move(path, path + ".old", true);
            File.AppendAllText(path, $"{DateTime.Now:O} {message}{Environment.NewLine}");
        }
        catch { }
    }
}