using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Threading;
using TD_Wallpaper.App;
using TD_Wallpaper.Core;

internal static class FilmstripChecks
{
    internal static int Run(string root, string[] args)
    {
        var count = int.Parse(args[Array.IndexOf(args, "--filmstrip") + 1]);
        var output = Path.GetFullPath(args[Array.IndexOf(args, "--filmstrip-output") + 1]);
        var directory = Path.Combine(Path.GetTempPath(), "TD_Wallpaper-filmstrip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        // Shared real media isolates container/layout cost from the separate thumbnail-cache step.
        var path = Program.FixtureImage(root);
        var state = new LibraryState();
        for (var i = 0; i < count; i++)
            state.Items.Add(new WallpaperItem { Id = i.ToString(), Name = $"Wallpaper {i:D5}", Path = path, Favorite = i % 2 == 0 });
        var failures = new List<string>(); var samples = new List<object>();
        var startup = Stopwatch.StartNew();
        var window = new MainWindow(state, new StateStore(directory), true) { Width = 1280, Height = 850 };
        void Check(bool pass, string name)
        {
            Console.WriteLine((pass ? "PASS " : "FAIL ") + "Filmstrip " + name);
            if (!pass) failures.Add(name);
        }
        window.Loaded += async (_, _) =>
        {
            try
            {
                var loadedMs = startup.Elapsed.TotalMilliseconds;
                var strip = (ListBox)window.FindName("Filmstrip");
                var list = (ListBox)window.FindName("WallpaperList");
                var preview = (WallpaperPreview)window.FindName("InlinePreview");
                var search = (TextBox)window.FindName("SearchBox");
                var sort = (ComboBox)window.FindName("SortBox");
                async Task Settle()
                {
                    await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ContextIdle);
                    await Task.Delay(80);
                    window.UpdateLayout();
                }
                async Task Ready(string id)
                {
                    var wait = Stopwatch.StartNew();
                    while (preview.SelectedId != id || preview.ImageSource == null)
                    {
                        if (wait.Elapsed.TotalSeconds > 20) throw new TimeoutException("preview " + id);
                        await Task.Delay(20);
                    }
                    await Settle();
                }
                int Realized() => Enumerable.Range(0, strip.Items.Count).Count(i => strip.ItemContainerGenerator.ContainerFromIndex(i) != null);
                void Sample(string stage)
                {
                    var realized = Realized();
                    var budget = Math.Min(strip.Items.Count, (int)Math.Ceiling(strip.ActualWidth / 87) * 3 + 6);
                    samples.Add(new { Stage = stage, Items = strip.Items.Count, Width = strip.ActualWidth, Realized = realized, Budget = budget });
                    Check(realized <= budget, stage + " containers bounded by viewport (" + realized + "/" + budget + ")");
                }
                void VisibleSelection(string stage)
                {
                    if (strip.SelectedItem == null) { Check(false, stage + " selected item exists"); return; }
                    var container = strip.ItemContainerGenerator.ContainerFromItem(strip.SelectedItem) as FrameworkElement;
                    var viewport = Descendants(strip).OfType<ScrollContentPresenter>().FirstOrDefault();
                    var visible = false;
                    if (container != null && viewport != null)
                    {
                        var bounds = container.TransformToAncestor(viewport).TransformBounds(new Rect(container.RenderSize));
                        visible = bounds.Left >= -0.5 && bounds.Right <= viewport.ActualWidth + 0.5 && bounds.IntersectsWith(new Rect(viewport.RenderSize));
                    }
                    Check(visible, stage + " selection is fully visible horizontally");
                }
                await Settle();
                if (count > 0) await Ready("0");
                Sample("initial");
                Check(strip.Items.Count == count && ReferenceEquals(strip.ItemsSource, list.ItemsSource), "full filtered sequence retained");
                using var process = Process.GetCurrentProcess(); process.Refresh();
                var privateMiB = process.PrivateMemorySize64 / 1048576d;
                var workingMiB = process.WorkingSet64 / 1048576d;
                if (count == 0)
                {
                    Check(strip.SelectedItem == null, "empty library has no selection");
                }
                else
                {
                    // Choose a distant item from the main list, then from the strip itself.
                    list.SelectedIndex = count - 1; await Ready((count - 1).ToString());
                    Check(ReferenceEquals(strip.SelectedItem, list.SelectedItem), "main selection syncs to strip");
                    VisibleSelection("last"); Sample("last");
                    var target = Math.Max(0, count - 2);
                    strip.SelectedIndex = target; await Ready(target.ToString());
                    Check(list.SelectedIndex == target && ReferenceEquals(strip.SelectedItem, list.SelectedItem), "strip selection syncs to main list and real preview");
                    VisibleSelection("strip click"); Sample("strip click");
                    // Scroll around a large collection without retaining all visited containers.
                    for (var i = 0; i < 12; i++)
                    {
                        list.SelectedIndex = (int)((long)i * (count - 1) / 11);
                        await Ready(list.SelectedIndex.ToString());
                    }
                    Sample("after distant selections"); VisibleSelection("after distant selections");
                    var scroller = Descendants(strip).OfType<ScrollViewer>().First();
                    var selectedBeforeScroll = list.SelectedItem;
                    for (var i = 0; i < 10; i++)
                    {
                        scroller.ScrollToHorizontalOffset(scroller.ScrollableWidth * i / 9);
                        await Settle();
                    }
                    Sample("after manual horizontal scroll");
                    Check(ReferenceEquals(list.SelectedItem, selectedBeforeScroll), "scrolling thumbnails does not change wallpaper selection");
                    scroller.ScrollToHorizontalOffset(scroller.ScrollableWidth / 2); await Settle();
                    if (scroller.ScrollableWidth > 0)
                    {
                        var visibleRow = Descendants(strip).OfType<ListBoxItem>().First(c => c.IsVisible);
                        var wheelOffset = scroller.HorizontalOffset;
                        visibleRow.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)
                        { RoutedEvent = Mouse.PreviewMouseWheelEvent });
                        await Settle();
                        Check(scroller.HorizontalOffset > wheelOffset, "mouse wheel scrolls the horizontal strip");
                        Sample("mouse wheel");
                    }
                    list.SelectedIndex = 0; await Ready("0");
                    window.Activate();
                    var focusedRow = (ListBoxItem)strip.ItemContainerGenerator.ContainerFromIndex(0);
                    focusedRow.Focus();
                    if (count > 1)
                    {
                        focusedRow.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(strip), Environment.TickCount, Key.Right)
                        { RoutedEvent = Keyboard.KeyDownEvent });
                        await Ready("1");
                        Check(strip.SelectedIndex == 1 && list.SelectedIndex == 1, "keyboard Right selects the next wallpaper");
                    }
                    var last = (WallpaperCard)list.Items[count - 1];
                    list.SelectedItem = last; await Ready(last.Item.Id);
                    search.Text = last.Name; await Task.Delay(230); await Settle();
                    Check(strip.Items.Count == 1 && ReferenceEquals(strip.SelectedItem, last), "search preserves selected card identity");
                    VisibleSelection("filtered");
                    search.Text = "no-filmstrip-results"; await Task.Delay(230); await Settle();
                    Check(strip.Items.Count == 0 && strip.SelectedItem == null, "empty filter clears stale strip selection");
                    search.Clear(); await Ready("0"); Sample("filter reset");
                    sort.SelectedIndex = 2; await Settle();
                    Check(strip.Items.Cast<WallpaperCard>().SequenceEqual(list.Items.Cast<WallpaperCard>()), "sort keeps both sequences identical");
                    var favorites = (CheckBox)window.FindName("OnlyFavorites");
                    favorites.IsChecked = true; await Settle();
                    Check(strip.Items.Count == (count + 1) / 2 && strip.Items.Cast<WallpaperCard>().All(c => c.Item.Favorite), "favorites filtering uses complete library");
                    favorites.IsChecked = false; sort.SelectedIndex = 0; await Settle();
                    list.SelectedItems.Clear(); list.SelectedItems.Add(list.Items[0]);
                    if (count > 1) list.SelectedItems.Add(list.Items[count - 1]);
                    await Settle();
                    Check(list.SelectedItems.Count == Math.Min(2, count), "multi-selection retained in main library");
                    list.SelectedItem = list.Items[count - 1]; await Settle();
                    window.Width = 1040; window.Height = 690; await Settle();
                    VisibleSelection("minimum window"); Sample("minimum window");
                    window.Width = 1600; window.Height = 950; await Settle();
                    VisibleSelection("large window"); Sample("large window");
                    // Existing navigation handlers; physical button tests run after the named controls exist.
                    var next = window.FindName("NextPreviewButton") as Button;
                    var previous = window.FindName("PreviousPreviewButton") as Button;
                    if (next != null && previous != null)
                    {
                        list.SelectedIndex = count - 1; await Ready((count - 1).ToString());
                        next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Ready("0");
                        Check(list.SelectedIndex == 0 && strip.SelectedIndex == 0, "next button wraps last to first");
                        previous.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Ready((count - 1).ToString());
                        Check(list.SelectedIndex == count - 1 && strip.SelectedIndex == count - 1, "previous button wraps first to last");
                        VisibleSelection("navigation"); Sample("navigation");
                    }
                    var selected = (WallpaperCard)list.SelectedItem;
                    var actions = (Button)window.FindName("SelectionActionsButton");
                    actions.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Settle();
                    Check(actions.ContextMenu!.IsOpen && strip.SelectedItem is WallpaperCard stripCard && selected.Item.Id == stripCard.Item.Id, "selected-item menu remains available after recycling");
                    actions.ContextMenu.IsOpen = false;
                    Check(!state.Settings.WasRunning, "inspection never starts desktop wallpaper");
                    if (count == 1000)
                    {
                        window.Width = 1280; window.Height = 850; await Settle();
                        VisibleSelection("capture after resize");
                        var capture = Path.ChangeExtension(output, ".png"); window.SaveRender(capture);
                        Console.WriteLine("CAPTURE " + capture);
                    }
                }
                var result = new { Items = count, WindowLoadedMs = loadedMs, MainPrivateMiB = privateMiB, MainWorkingSetMiB = workingMiB,
                    Samples = samples, Failures = failures, TestDirectory = directory,
                    Note = "1280x850 initially; shared real JPEG (one deduplicated thumbnail), distinct item IDs; isolates UI containers, not full thumbnail RAM budget; no desktop playback; one run." };
                var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
                // Use TEMP then shell-copy when host policy restricts runtime writes in workspace.
                var temporary = Path.Combine(directory, Path.GetFileName(output)); File.WriteAllText(temporary, json);
                File.Copy(temporary, output, true);
                Console.WriteLine("FILMSTRIP RESULT " + temporary);
            }
            catch (Exception ex) { failures.Add(ex.ToString()); Console.WriteLine("FAIL Filmstrip " + ex); }
            finally { window.Exit(); Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        };
        window.Show(); Dispatcher.Run();
        return failures.Count == 0 ? 0 : 1;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}