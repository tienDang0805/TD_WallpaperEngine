namespace TienDang.Core;

public sealed class Rotation(Random? random = null)
{
    private readonly Random _random = random ?? Random.Shared;
    private readonly Queue<string> _bag = new();
    private string _signature = "";
    private readonly List<string> _history = [];
    private int _historyIndex = -1;
    public string? CurrentId { get; private set; }

    // A selected item is part of the first shuffle round, not an extra random pick.
    public WallpaperItem? Start(IReadOnlyList<WallpaperItem> items, bool shuffle, string? preferredId = null)
    {
        ResetCycle();
        var selected = items.FirstOrDefault(i => i.Id == preferredId);
        if (selected == null) return Next(items, shuffle);
        SetCurrent(selected.Id);
        if (shuffle)
        {
            _signature = string.Join("|", items.Select(i => i.Id));
            var remaining = items.Where(i => i.Id != selected.Id).Select(i => i.Id).ToList();
            for (var i = remaining.Count - 1; i > 0; i--) { var j = _random.Next(i + 1); (remaining[i], remaining[j]) = (remaining[j], remaining[i]); }
            foreach (var id in remaining) _bag.Enqueue(id);
        }
        return selected;
    }
    public void SetCurrent(string id)
    {
        if (CurrentId == id) return;
        CurrentId = id;
        if (_historyIndex < _history.Count - 1) _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
        _history.Add(id);
        if (_history.Count > 200) _history.RemoveAt(0);
        _historyIndex = _history.Count - 1;
    }

    public WallpaperItem? Next(IReadOnlyList<WallpaperItem> items, bool shuffle)
    {
        if (items.Count == 0) return null;
        if (_historyIndex < _history.Count - 1)
        {
            while (++_historyIndex < _history.Count)
            {
                var forward = items.FirstOrDefault(i => i.Id == _history[_historyIndex]);
                if (forward != null) { CurrentId = forward.Id; return forward; }
            }
            _historyIndex = _history.Count - 1;
        }
        WallpaperItem result;
        if (!shuffle)
        {
            var index = items.ToList().FindIndex(i => i.Id == CurrentId);
            result = items[(index + 1) % items.Count];
        }
        else
        {
            var signature = string.Join("|", items.Select(i => i.Id));
            if (_signature != signature) { _signature = signature; _bag.Clear(); }
            if (_bag.Count == 0)
            {
                var ids = items.Select(i => i.Id).ToList();
                for (var i = ids.Count - 1; i > 0; i--) { var j = _random.Next(i + 1); (ids[i], ids[j]) = (ids[j], ids[i]); }
                if (ids.Count > 1 && ids[0] == CurrentId) (ids[0], ids[1]) = (ids[1], ids[0]);
                foreach (var id in ids) _bag.Enqueue(id);
            }
            // Manual pinning can place the current item at the front of a remaining bag.
            if (_bag.Count > 0 && _bag.Peek() == CurrentId && items.Count > 1)
            {
                _bag.Dequeue();
                if (_bag.Count == 0)
                {
                    var other = items.Where(i => i.Id != CurrentId).OrderBy(_ => _random.Next()).Select(i => i.Id);
                    foreach (var id in other) _bag.Enqueue(id);
                }
            }
            var nextId = _bag.Dequeue();
            result = items.First(i => i.Id == nextId);
        }
        SetCurrent(result.Id);
        return result;
    }

    public void ResetCycle()
    {
        _bag.Clear();
        _signature = "";
        _history.Clear();
        _historyIndex = -1;
        CurrentId = null;
    }

    public WallpaperItem? Previous(IReadOnlyList<WallpaperItem> items)
    {
        while (_historyIndex > 0)
        {
            _historyIndex--;
            var previousId = _history[_historyIndex];
            var found = items.FirstOrDefault(i => i.Id == previousId);
            if (found != null) { CurrentId = found.Id; return found; }
        }
        return null;
    }
}

public sealed class PlaybackClock
{
    public double RemainingSeconds { get; private set; }
    public void Reset(double seconds) => RemainingSeconds = Math.Max(0, seconds);
    public bool Advance(double elapsedSeconds, bool paused)
    {
        if (paused) return false;
        RemainingSeconds = Math.Max(0, RemainingSeconds - Math.Max(0, elapsedSeconds));
        return RemainingSeconds <= 0;
    }
}