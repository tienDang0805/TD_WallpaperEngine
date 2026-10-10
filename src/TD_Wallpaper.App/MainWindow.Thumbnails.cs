using System.Windows.Threading;
namespace TD_Wallpaper.App;
public partial class MainWindow
{
    private readonly Dictionary<ListBoxItem, WallpaperCard> _thumbnailRows = [];
    private readonly Dictionary<WallpaperCard, CancellationTokenSource> _thumbnailRequests = [];
    private readonly HashSet<WallpaperCard> _wantedThumbnails = [];
    private bool _thumbnailRefreshQueued;
    internal long ThumbnailCacheBytes => _thumbnails.CacheBytes;
    internal long ThumbnailPeakCacheBytes => _thumbnails.PeakCacheBytes;
    internal int ThumbnailWantedCount => _wantedThumbnails.Count;
    internal int ThumbnailPendingRequests => _thumbnailRequests.Count;
    internal int ThumbnailPendingJobs => _thumbnails.PendingJobs;
    internal int ThumbnailPeakActiveJobs => _thumbnails.PeakActiveJobs;
    internal int ThumbnailStartedJobs => _thumbnails.StartedJobs;
    private void ThumbnailRowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ListBoxItem row) return;
        row.DataContextChanged -= ThumbnailRowChanged; row.DataContextChanged += ThumbnailRowChanged;
        if (row.DataContext is WallpaperCard card) _thumbnailRows[row] = card;
        QueueThumbnailRefresh();
    }
    private void ThumbnailRowUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ListBoxItem row) return;
        row.DataContextChanged -= ThumbnailRowChanged; _thumbnailRows.Remove(row); QueueThumbnailRefresh();
    }
    private void ThumbnailRowChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not ListBoxItem row) return;
        if (e.NewValue is WallpaperCard card) _thumbnailRows[row] = card; else _thumbnailRows.Remove(row);
        QueueThumbnailRefresh();
    }
    private void QueueThumbnailRefresh()
    {
        if (_exiting || !_initialized || _thumbnailRefreshQueued) return;
        _thumbnailRefreshQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () => { _thumbnailRefreshQueued = false; RefreshVisibleThumbnails(); });
    }
    private void RefreshVisibleThumbnails()
    {
        if (_exiting) return;
        var wanted = IsVisible ? _thumbnailRows.Where(r => r.Key.IsLoaded).Select(r => r.Value).ToHashSet() : [];
        var selected = WallpaperList.SelectedItem as WallpaperCard;
        if (IsVisible && selected != null) wanted.Add(selected);
        foreach (var card in _wantedThumbnails.Where(c => !wanted.Contains(c)).ToArray())
        {
            if (_thumbnailRequests.Remove(card, out var request)) { request.Cancel(); request.Dispose(); }
            card.ReleaseThumbnail();
        }
        _wantedThumbnails.Clear(); _wantedThumbnails.UnionWith(wanted);
        foreach (var card in wanted.OrderByDescending(c => c == selected))
        {
            if (card.Thumbnail != null || card.ThumbnailAttempted || _thumbnailRequests.ContainsKey(card)) continue;
            var request = new CancellationTokenSource(); _thumbnailRequests.Add(card, request); _ = LoadVisibleThumbnailAsync(card, request);
        }
    }
    private async Task LoadVisibleThumbnailAsync(WallpaperCard card, CancellationTokenSource request)
    {
        var token = request.Token; var path = card.Path;
        try
        {
            var available = await Task.Run(() => File.Exists(path), token);
            var image = available ? await _thumbnails.GetAsync(card.Item, token) : null;
            if (!_exiting && !token.IsCancellationRequested && _wantedThumbnails.Contains(card) && card.Path == path)
            { card.SetFileAvailable(available); card.SetThumbnail(image); }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (_thumbnailRequests.TryGetValue(card, out var current) && current == request)
            { _thumbnailRequests.Remove(card); request.Dispose(); }
        }
    }
    private void ReleaseThumbnails()
    {
        foreach (var request in _thumbnailRequests.Values) { request.Cancel(); request.Dispose(); }
        _thumbnailRequests.Clear(); foreach (var card in _wantedThumbnails) card.ReleaseThumbnail(); _wantedThumbnails.Clear(); _thumbnails.ClearMemoryCache();
    }
}