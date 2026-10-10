namespace TienDang.App;

public partial class MainWindow
{
    private static readonly uint TaskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
    private void RefreshTaskbar(uint message)
    {
        if (_smoke || _exiting || (message != TaskbarCreatedMessage && message is not (0x31A or 0x31E or 0x320))) return;
        try { _taskbar.Refresh(); } catch (Exception error) { _store.Log("Taskbar refresh: " + error.Message); }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string name);
    private async void ToggleTransparentTaskbar(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _taskbarBusy) return;
        await SetTaskbarPreferenceAsync(TransparentTaskbarBox.IsChecked == true);
    }
    internal async Task<bool> SetTaskbarPreferenceAsync(bool enabled)
    {
        if (_taskbarBusy) return false;
        _taskbarBusy = true; TransparentTaskbarBox.IsEnabled = false;
        var previous = _state.Settings.TransparentTaskbar;
        var success = false;
        try
        {
            if (!CommitMutation(() => _state.Settings.TransparentTaskbar = enabled,
                () => _state.Settings.TransparentTaskbar = previous)) return false;
            try
            {
                if (!_smoke) await _taskbar.SetEnabledAsync(enabled);
                success = true; return true;
            }
            catch (Exception error)
            {
                _store.Log("Taskbar appearance: " + error);
                CommitMutation(() => _state.Settings.TransparentTaskbar = previous,
                    () => _state.Settings.TransparentTaskbar = enabled);
                ShowNotice(error.Message); return false;
            }
        }
        finally
        {
            TransparentTaskbarBox.IsChecked = success ? enabled : previous;
            TransparentTaskbarBox.IsEnabled = true; _taskbarBusy = false;
        }
    }
    private async Task RestoreTaskbarAsync()
    {
        if (!_state.Settings.TransparentTaskbar) return;
        _taskbarBusy = true; TransparentTaskbarBox.IsEnabled = false;
        try { await _taskbar.SetEnabledAsync(true); }
        catch (Exception error)
        {
            _store.Log("Taskbar appearance restore: " + error); ShowNotice(error.Message);
            if (CommitMutation(() => _state.Settings.TransparentTaskbar = false, () => _state.Settings.TransparentTaskbar = true))
                TransparentTaskbarBox.IsChecked = false;
        }
        finally { TransparentTaskbarBox.IsEnabled = true; _taskbarBusy = false; }
    }
}
