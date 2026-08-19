using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using Lucent.Bookmarks;
using Lucent.Home;
using Lucent.Ui;
using Lucent.Updates;
using Microsoft.Web.WebView2.Core;

namespace Lucent;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<BrowserTab> _tabs = new();
    private readonly BookmarkStore _bookmarks = new();
    private readonly VisitStore _visits = new();
    private readonly WindowStateStore _window = new();
    private readonly HistoryStore _history = new();
    private readonly TabSession _session = new();
    private HomePage? _home;

    private WindowState _lastVisibleState = WindowState.Normal;

    private IntPtr _lastMonitor;

    private CoreWebView2Environment? _environment;
    private BrowserTab? _active;
    private UpdateInfo? _update;
    private bool _updateOffered;
    private bool _isFullScreen;
    private WindowState _preFullScreenState = WindowState.Normal;

    public ICommand NewTabCommand { get; }
    public ICommand CloseTabCommand { get; }
    public ICommand FocusAddressCommand { get; }
    public ICommand ReloadCommand { get; }
    public ICommand BookmarkCommand { get; }
    public ICommand ToggleBookmarkBarCommand { get; }

    public MainWindow()
    {
        InitializeComponent();

        NewTabCommand = new RelayCommand(() => _ = OpenTabAsync(Url.Home, activate: true));
        CloseTabCommand = new RelayCommand(() => { if (_active is not null) CloseTab(_active); });
        FocusAddressCommand = new RelayCommand(() => { AddressBar.Focus(); AddressBar.SelectAll(); });
        ReloadCommand = new RelayCommand(() => _active?.View.CoreWebView2?.Reload());
        BookmarkCommand = new RelayCommand(ToggleBookmark);
        ToggleBookmarkBarCommand = new RelayCommand(() =>
        {
            _bookmarks.BarVisible = !_bookmarks.BarVisible;
            _bookmarks.Save();
            ShowBookmarkBar();
        });

        _bookmarks.Load();
        BookmarkBar.ItemsSource = _bookmarks.Items;
        ShowBookmarkBar();

        _visits.Load();
        _history.Load();
        _session.Load();
        _home = new HomePage(_bookmarks, _visits, _history);

        _window.Load();

        TabStrip.ItemsSource = _tabs;
        Loaded += OnLoaded;
        StateChanged += (_, _) => { UpdateContentInset(); UpdateResizeBorder(); RememberWindowState(); };
        LocationChanged += (_, _) => RememberMonitor();
        Closing += (_, _) =>
        {
            _window.Maximized = _lastVisibleState == WindowState.Maximized;

            if (RestoreBounds.Width > 0 && RestoreBounds.Height > 0)
                _window.Bounds = RestoreBounds;

            _window.Save();

            SaveSession();

            _history.Flush();
        };
        Closed += (_, _) => { foreach (BrowserTab tab in _tabs) tab.Dispose(); };
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyRoundedCorners();
        UpdateContentInset();

        UpdateResizeBorder();

        VersionLabel.Text = $"v{Release.CurrentDisplay}";

        var options = new CoreWebView2EnvironmentOptions
        {
            AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required"
        };

        try
        {
            _environment = await CoreWebView2Environment.CreateAsync(null, App.UserDataFolder, options);
        }
        catch (Exception ex)
        {
            ShowStartupFailure(ex);
            return;
        }

        await RestoreSessionAsync();

        _ = CheckForUpdateAsync();
    }

    private void ShowStartupFailure(Exception ex)
    {
        MessageBoxResult answer = MessageBox.Show(
            "Lucent needs the Microsoft WebView2 runtime, which does not appear to be installed.\n\n" +
            "Open the download page now?\n\n" +
            $"({ex.Message})",
            "Lucent",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer == MessageBoxResult.Yes)
            OpenExternally(Release.WebView2Download);

        Close();
    }

    private static void OpenExternally(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });


    private async Task CheckForUpdateAsync()
    {
        _update = await Updater.CheckAsync();
        if (_update is null) return;

        UpdateText.Text = $"Lucent {_update.Display} Is Available";
        _updateOffered = true;
        ShowUpdateBar();
    }

    private void ShowUpdateBar() =>
        UpdateBar.Visibility = _updateOffered && !_isFullScreen ? Visibility.Visible : Visibility.Collapsed;

    private async void UpdateInstall_Click(object sender, RoutedEventArgs e)
    {
        if (_update is null) return;

        UpdateAction.IsEnabled = false;
        UpdateDismiss.IsEnabled = false;

        var progress = new Progress<double>(fraction =>
            UpdateText.Text = $"Downloading Lucent {_update.Display}... {fraction:P0}");

        try
        {
            string package = await Updater.DownloadAsync(_update, progress);

            UpdateText.Text = "Restarting...";
            Updater.ApplyAndRestart(package);
        }
        catch (Exception ex)
        {
            UpdateText.Text = "Update failed.";
            UpdateAction.Content = "Open Page";
            UpdateAction.Click -= UpdateInstall_Click;
            UpdateAction.Click += (_, _) => OpenExternally(Release.ReleasesPage);
            UpdateAction.IsEnabled = true;
            UpdateDismiss.IsEnabled = true;

            MessageBox.Show(ex.Message, "Lucent", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void UpdateLater_Click(object sender, RoutedEventArgs e)
    {
        _updateOffered = false;
        ShowUpdateBar();
    }


    private async Task RestoreSessionAsync()
    {
        if (_session.Urls.Count == 0)
        {
            await OpenTabAsync(Url.Home, activate: true);
            return;
        }

        for (int i = 0; i < _session.Urls.Count; i++)
            await OpenTabAsync(_session.Urls[i], activate: false);

        SelectTab(_tabs[Math.Clamp(_session.Active, 0, _tabs.Count - 1)]);
    }

    private void SaveSession()
    {
        _session.Save(_tabs.Select(t => string.IsNullOrWhiteSpace(t.Source) ? Url.Home : t.Source),
                      _active is null ? 0 : Math.Max(0, _tabs.IndexOf(_active)));
    }

    private async Task<BrowserTab> OpenTabAsync(string? url, bool activate)
    {
        var tab = new BrowserTab { Home = _home, Visits = _visits, History = _history };
        _tabs.Add(tab);
        ContentHost.Children.Add(tab.View);

        tab.Changed += OnTabChanged;
        tab.FullScreenChanged += OnFullScreenChanged;
        tab.NewWindowRequested += OnNewWindowRequested;
        tab.ContextMenuRequested += OnContextMenuRequested;
        tab.Blocker.BlockedCountChanged += _ => Dispatcher.BeginInvoke(UpdateChrome);

        await tab.InitializeAsync(_environment!, url);

        if (activate) SelectTab(tab);
        else tab.IsActive = false;

        return tab;
    }

    private void SelectTab(BrowserTab tab)
    {
        foreach (BrowserTab other in _tabs)
            other.IsActive = ReferenceEquals(other, tab);

        _active = tab;
        UpdateChrome();
    }

    private void CloseTab(BrowserTab tab)
    {
        int index = _tabs.IndexOf(tab);
        if (index < 0) return;

        _tabs.Remove(tab);
        ContentHost.Children.Remove(tab.View);
        tab.Dispose();

        if (_tabs.Count == 0)
        {
            Close();
            return;
        }

        if (ReferenceEquals(_active, tab))
            SelectTab(_tabs[Math.Min(index, _tabs.Count - 1)]);

        SaveSession();
    }

    private void OnTabChanged(BrowserTab tab)
    {
        if (ReferenceEquals(tab, _active)) UpdateChrome();

        SaveSession();
    }

    private async void OnNewWindowRequested(BrowserTab source, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        using CoreWebView2Deferral deferral = e.GetDeferral();

        BrowserTab tab = await OpenTabAsync(null, activate: false);
        e.NewWindow = tab.View.CoreWebView2;
    }

    private void OnContextMenuRequested(BrowserTab source, CoreWebView2ContextMenuRequestedEventArgs e) =>
        PageContextMenu.Show(source.View, e);


    private void UpdateChrome()
    {
        if (_active is null) return;

        BackButton.IsEnabled = _active.CanGoBack;
        ForwardButton.IsEnabled = _active.CanGoForward;

        if (!AddressBar.IsKeyboardFocusWithin)
            AddressBar.Text = HomePage.IsHome(_active.Source) ? string.Empty : _active.Source;

        BlockCount.Text = $"{_active.Blocker.BlockedCount} blocked";
        Title = string.IsNullOrWhiteSpace(_active.Title) || HomePage.IsHome(_active.Source)
            ? "Lucent"
            : $"{_active.Title} - Lucent";

        UpdateStar();
    }


    private const string StarOutline = "";
    private const string StarFilled = "";

    private void UpdateStar()
    {
        StarButton.Visibility = HomePage.IsHome(_active?.Source)
            ? Visibility.Collapsed
            : Visibility.Visible;

        bool saved = _bookmarks.Contains(_active?.Source);

        StarButton.Content = saved ? StarFilled : StarOutline;
        StarButton.Foreground = saved
            ? (Brush)FindResource("Accent")
            : (Brush)FindResource("FgDim");
        StarButton.ToolTip = saved ? "Remove Bookmark (Ctrl+D)" : "Bookmark This Page (Ctrl+D)";
    }

    private void ToggleBookmark()
    {
        if (_active is null) return;

        string url = _active.Source;
        if (string.IsNullOrWhiteSpace(url)) return;

        if (_bookmarks.Contains(url))
        {
            _bookmarks.Remove(url);
        }
        else
        {
            _bookmarks.Add(url, _active.Title, _active.Favicon);

            _bookmarks.BarVisible = true;
            _bookmarks.Save();
        }

        ShowBookmarkBar();
        UpdateStar();
    }

    private void ShowBookmarkBar() =>
        BookmarkBarRoot.Visibility = _bookmarks.BarVisible && !_isFullScreen && _bookmarks.Items.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

    private void Star_Click(object sender, RoutedEventArgs e) => ToggleBookmark();

    private void Bookmark_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not Bookmark bookmark) return;

        if (_active is null) _ = OpenTabAsync(bookmark.Url, activate: true);
        else _active.Navigate(bookmark.Url);
    }

    private void BookmarkRename_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not Bookmark bookmark) return;

        var dialog = new RenameDialog(bookmark.Url, bookmark.Title) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        _bookmarks.Rename(bookmark.Url, dialog.EnteredName);
    }

    private void BookmarkRemove_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not Bookmark bookmark) return;

        _bookmarks.Remove(bookmark.Url);
        ShowBookmarkBar();
        UpdateStar();
    }

    private void UpdateContentInset()
    {
        bool edgeToEdge = _isFullScreen || WindowState == WindowState.Maximized;
        ContentHost.Margin = edgeToEdge ? new Thickness(0) : new Thickness(4, 0, 4, 4);
    }

    private void OnFullScreenChanged(BrowserTab tab, bool isFullScreen)
    {
        if (ReferenceEquals(tab, _active)) SetFullScreen(isFullScreen);
    }

    private void SetFullScreen(bool on)
    {
        if (on == _isFullScreen) return;
        _isFullScreen = on;

        TabStripRow.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        NavRow.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        ShowUpdateBar();
        ShowBookmarkBar();

        SetCaptionStyle(!on);

        if (on)
        {
            _preFullScreenState = WindowState;
            WindowState = WindowState.Normal;
            WindowState = WindowState.Maximized;
        }
        else
        {
            WindowState = WindowState.Normal;
            WindowState = _preFullScreenState;
        }

        UpdateContentInset();
    }


    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        IntPtr handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(handle)?.AddHook(WndProc);

        SetCaptionStyle(true);

        RestoreSavedPlacement();
    }

    private void SetCaptionStyle(bool on)
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;

        const int WindowStyle = -16;
        const int Caption = 0x00C00000;

        int style = GetWindowLong(handle, WindowStyle);
        if (style == 0) return;

        SetWindowLong(handle, WindowStyle, on ? style | Caption : style & ~Caption);

        if (IsVisible)
        {
            const uint NoSize = 0x0001, NoMove = 0x0002, NoZOrder = 0x0004;
            const uint FrameChanged = 0x0020, NoActivate = 0x0010;

            SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0,
                         NoSize | NoMove | NoZOrder | NoActivate | FrameChanged);
        }
    }

    private void RestoreSavedPlacement()
    {
        if (_window.Bounds is { } bounds && IsOnSomeDisplay(bounds))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;

            Left = bounds.Left;
            Top = bounds.Top;
            Width = bounds.Width;
            Height = bounds.Height;
        }

        if (!_window.Maximized) return;

        WindowState = WindowState.Maximized;
        _lastVisibleState = WindowState.Maximized;
    }

    private static bool IsOnSomeDisplay(Rect bounds)
    {
        var rect = new NativeRect
        {
            left = (int)bounds.Left,
            top = (int)bounds.Top,
            right = (int)(bounds.Left + bounds.Width),
            bottom = (int)(bounds.Top + bounds.Height)
        };

        return MonitorFromRect(ref rect, MonitorDefaultToNull) != IntPtr.Zero;
    }

    private void RememberWindowState()
    {
        if (_isFullScreen || WindowState == WindowState.Minimized) return;

        _lastVisibleState = WindowState;
    }

    private void RememberMonitor()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        IntPtr monitor = MonitorFromWindow(hwnd, MonitorDefaultToNull);
        if (monitor != IntPtr.Zero) _lastMonitor = monitor;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WmGetMinMaxInfo = 0x0024;
        if (msg != WmGetMinMaxInfo) return IntPtr.Zero;

        IntPtr monitor = MonitorFromWindow(hwnd, MonitorDefaultToNull);

        if (monitor == IntPtr.Zero) monitor = _lastMonitor;
        if (monitor == IntPtr.Zero) monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero) return IntPtr.Zero;

        var info = new NativeMonitorInfo { cbSize = Marshal.SizeOf<NativeMonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero) return IntPtr.Zero;

            info = new NativeMonitorInfo { cbSize = Marshal.SizeOf<NativeMonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref info)) return IntPtr.Zero;
        }

        if (MonitorFromWindow(hwnd, MonitorDefaultToNull) is { } onScreen && onScreen != IntPtr.Zero)
            _lastMonitor = onScreen;

        NativeRect area = _isFullScreen ? info.rcMonitor : info.rcWork;
        var mmi = Marshal.PtrToStructure<NativeMinMaxInfo>(lParam);

        mmi.ptMaxPosition.x = area.left - info.rcMonitor.left;
        mmi.ptMaxPosition.y = area.top - info.rcMonitor.top;
        mmi.ptMaxSize.x = area.right - area.left;
        mmi.ptMaxSize.y = area.bottom - area.top;
        mmi.ptMaxTrackSize.x = mmi.ptMaxSize.x;
        mmi.ptMaxTrackSize.y = mmi.ptMaxSize.y;

        Marshal.StructureToPtr(mmi, lParam, true);
        handled = true;
        return IntPtr.Zero;
    }

    private void ApplyRoundedCorners()
    {
        const int DwmwaWindowCornerPreference = 33;
        const int CornerPreferenceRound = 2;

        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        int preference = CornerPreferenceRound;
        DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref preference, sizeof(int));
    }

    private const int MonitorDefaultToNearest = 2;

    private const int MonitorDefaultToNull = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int x, y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMinMaxInfo
    {
        public NativePoint ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMonitorInfo
    {
        public int cbSize;
        public NativeRect rcMonitor;
        public NativeRect rcWork;
        public int dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref NativeRect rect, int flags);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after,
                                            int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref NativeMonitorInfo info);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);


    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();


    private BrowserTab? _dragTab;
    private Point _dragOrigin;
    private bool _dragStarted;

    private const double DragThreshold = 4.0;

    private void Tab_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not BrowserTab tab) return;

        SelectTab(tab);

        _dragTab = tab;
        _dragOrigin = e.GetPosition(this);
        _dragStarted = false;

        ((UIElement)sender).CaptureMouse();
    }

    private void Tab_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragTab is null || e.LeftButton != MouseButtonState.Pressed) return;

        Point here = e.GetPosition(this);

        if (!_dragStarted)
        {
            Vector moved = here - _dragOrigin;
            if (Math.Abs(moved.X) < DragThreshold && Math.Abs(moved.Y) < DragThreshold) return;

            _dragStarted = true;
            if (_tabs.Count > 1) _dragTab.IsDragging = true;
        }

        if (_tabs.Count > 1 && IsOverTabStrip(here))
        {
            _dragTab.DragOffset = here.X - _dragOrigin.X;

            if (TabUnder(here) is { } target)
            {
                _tabs.Move(_tabs.IndexOf(_dragTab), _tabs.IndexOf(target));

                _dragOrigin = here;
                _dragTab.DragOffset = 0;
            }
            return;
        }

        EndTabDrag(sender);

        RestoreBeforeDrag(here);

        try { DragMove(); } catch (InvalidOperationException) { }
    }

    private void Tab_MouseUp(object sender, MouseButtonEventArgs e) => EndTabDrag(sender);

    private void Tab_LostCapture(object sender, MouseEventArgs e) => EndTabDrag(sender);

    private void RestoreBeforeDrag(Point inWindow)
    {
        if (WindowState != WindowState.Maximized) return;

        double acrossWindow = ActualWidth > 0 ? inWindow.X / ActualWidth : 0.5;

        GetCursorPos(out NativePoint cursor);
        double restoredWidth = RestoreBounds.Width > 0 ? RestoreBounds.Width : Width;

        WindowState = WindowState.Normal;

        Left = cursor.x - restoredWidth * acrossWindow;
        Top = cursor.y - inWindow.Y;
    }

    private void UpdateResizeBorder()
    {
        if (WindowChrome.GetWindowChrome(this) is not { } chrome) return;

        chrome.ResizeBorderThickness = WindowState == WindowState.Maximized
            ? new Thickness(0)
            : new Thickness(6);
    }

    private void EndTabDrag(object sender)
    {
        if (sender is UIElement element && element.IsMouseCaptured) element.ReleaseMouseCapture();

        if (_dragTab is not null)
        {
            _dragTab.IsDragging = false;
            _dragTab.DragOffset = 0;        }

        _dragTab = null;
        _dragStarted = false;
    }

    private bool IsOverTabStrip(Point point)
    {
        Point topLeft = TabStripRow.TranslatePoint(new Point(0, 0), this);
        return point.Y >= topLeft.Y && point.Y <= topLeft.Y + TabStripRow.ActualHeight;
    }

    private BrowserTab? TabUnder(Point inWindow)
    {
        for (int i = 0; i < _tabs.Count; i++)
        {
            if (ReferenceEquals(_tabs[i], _dragTab)) continue;
            if (TabStrip.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement slot) continue;

            Point topLeft = slot.TranslatePoint(new Point(0, 0), this);
            if (inWindow.X >= topLeft.X && inWindow.X <= topLeft.X + slot.ActualWidth) return _tabs[i];
        }

        return null;
    }

    private void TabClose_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is BrowserTab tab) CloseTab(tab);
        e.Handled = true;
    }

    private void NewTab_Click(object sender, RoutedEventArgs e) => _ = OpenTabAsync(Url.Home, true);

    private void Back_Click(object sender, RoutedEventArgs e) => _active?.View.CoreWebView2?.GoBack();

    private void Forward_Click(object sender, RoutedEventArgs e) => _active?.View.CoreWebView2?.GoForward();

    private void Reload_Click(object sender, RoutedEventArgs e) => _active?.View.CoreWebView2?.Reload();

    private void AddressBar_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _active is null) return;

        _active.Navigate(AddressBar.Text);
        _active.View.Focus();
        e.Handled = true;
    }

    private void AddressBar_GotFocus(object sender, KeyboardFocusChangedEventArgs e) => AddressBar.SelectAll();
}
