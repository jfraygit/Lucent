using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Lucent.Bookmarks;
using Lucent.Home;
using Lucent.Ui;
using Lucent.Updates;
using Microsoft.Web.WebView2.Core;

namespace Lucent;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<BrowserTab> _tabs = new();
    private readonly Browser _browser;

    private BookmarkStore _bookmarks => _browser.Bookmarks;
    private VisitStore _visits => _browser.Visits;
    private WindowStateStore _window => _browser.WindowState;
    private HistoryStore _history => _browser.History;
    private TabSession _session => _browser.Session;
    private HomePage _home => _browser.Home;

    private readonly bool _isFirst;

    public static MainWindow? Recent { get; private set; }

    private WindowState _lastVisibleState = WindowState.Normal;

    private IntPtr _lastMonitor;

    private CoreWebView2Environment? _environment;
    private BrowserTab? _active;
    private UpdateInfo? _update;
    private bool _updateOffered;
    private bool _defaultOffered;

    private bool _defaultSent;

    private bool _isFullScreen;
    private WindowState _preFullScreenState = WindowState.Normal;

    public ICommand NewTabCommand { get; }
    public ICommand CloseTabCommand { get; }
    public ICommand FocusAddressCommand { get; }
    public ICommand ReloadCommand { get; }
    public ICommand BookmarkCommand { get; }
    public ICommand ToggleBookmarkBarCommand { get; }

    public MainWindow(Browser browser, bool isFirst = true)
    {
        _browser = browser;
        _isFirst = isFirst;

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

        BookmarkBar.ItemsSource = _bookmarks.Items;
        ShowBookmarkBar();

        _bookmarks.Items.CollectionChanged += OnBookmarksChanged;

        TabStrip.ItemsSource = _tabs;
        Loaded += OnLoaded;
        StateChanged += (_, _) =>
        {
            UpdateContentInset();
            UpdateMaximizedInset();
            UpdateResizeBorder();
            ApplyRoundedCorners();
            RememberWindowState();
        };
        LocationChanged += (_, _) => RememberMonitor();
        Activated += (_, _) =>
        {
            Recent = this;

            RefreshDefaultBar();
        };
        Closing += (_, _) =>
        {
            SaveSession(closing: this);

            _window.Maximized = _lastVisibleState == WindowState.Maximized;

            if (RestoreBounds.Width > 0 && RestoreBounds.Height > 0)
                _window.Bounds = RestoreBounds;

            if (_isFirst) _window.Save();
        };
        Closed += (_, _) =>
        {
            _bookmarks.Items.CollectionChanged -= OnBookmarksChanged;

            if (ReferenceEquals(Recent, this))
                Recent = Application.Current.Windows.OfType<MainWindow>()
                                    .FirstOrDefault(w => !ReferenceEquals(w, this));

            foreach (BrowserTab tab in _tabs) tab.Dispose();
        };
    }

    private void OnBookmarksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ShowBookmarkBar();
        UpdateStar();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyRoundedCorners();
        UpdateContentInset();
        UpdateMaximizedInset();

        UpdateResizeBorder();

        VersionLabel.Text = $"v{Release.CurrentDisplay}";

        try
        {
            _environment = await _browser.EnvironmentAsync();
        }
        catch (Exception ex)
        {
            ShowStartupFailure(ex);
            return;
        }

        if (_isFirst && !_browser.SessionRestored)
        {
            _browser.SessionRestored = true;
            await RestoreSessionAsync();
        }

        if (_browser.Pending is { } pending)
        {
            _browser.Pending = null;
            await OpenTabAsync(pending, activate: true);
        }

        if (_tabs.Count == 0)
            await OpenTabAsync(Url.Home, activate: true);

        if (!_browser.UpdateChecked)
        {
            _browser.UpdateChecked = true;
            _ = CheckForUpdateAsync();
        }

        OfferToBeDefault();
    }

    public async void OpenFromLaunch(string? url)
    {
        if (WindowState == WindowState.Minimized) WindowState = _lastVisibleState;

        Activate();

        if (url is null) return;

        if (_environment is null)
        {
            _browser.Pending = url;
            return;
        }

        await OpenTabAsync(url, activate: true);
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


    private void OfferToBeDefault()
    {
        if (!_isFirst) return;

        if (DefaultBrowser.IsDefault || DefaultBrowser.Dismissed) return;

        DefaultText.Text = "Make Lucent Your Default Browser";
        _defaultOffered = true;
        ShowDefaultBar();
    }

    private void ShowDefaultBar() =>
        DefaultBar.Visibility = _defaultOffered && !_isFullScreen ? Visibility.Visible : Visibility.Collapsed;

    private void RefreshDefaultBar()
    {
        if (!_defaultOffered || !DefaultBrowser.IsDefault) return;

        _defaultOffered = false;
        _defaultSent = false;
        ShowDefaultBar();
    }

    private void DefaultSet_Click(object sender, RoutedEventArgs e)
    {
        if (!DefaultBrowser.Register())
        {
            DefaultText.Text = "Lucent Could Not Register Itself With Windows.";
            DefaultAction.Visibility = Visibility.Collapsed;
            return;
        }

        DefaultBrowser.OpenSettings();

        _defaultSent = true;
        DefaultText.Text = "Set Lucent For HTTP And HTTPS In Settings.";
        DefaultAction.Visibility = Visibility.Collapsed;
        DefaultDismiss.Content = "Hide";
    }

    private void DefaultLater_Click(object sender, RoutedEventArgs e)
    {
        if (!_defaultSent) DefaultBrowser.Dismiss();

        _defaultOffered = false;
        ShowDefaultBar();
    }


    private async Task RestoreSessionAsync()
    {
        IReadOnlyList<SessionWindow> windows = _session.Windows;

        if (windows.Count == 0)
        {
            await OpenTabAsync(Url.Home, activate: true);
            return;
        }

        await FillAsync(windows[0]);

        for (int i = 1; i < windows.Count; i++)
        {
            var window = new MainWindow(_browser, isFirst: false);
            window.Show();
            await window.FillAsync(windows[i]);
        }

        Activate();
    }

    private async Task FillAsync(SessionWindow saved)
    {
        foreach (string url in saved.Urls)
            await OpenTabAsync(url, activate: false);

        if (_tabs.Count > 0)
            SelectTab(_tabs[Math.Clamp(saved.Active, 0, _tabs.Count - 1)]);
    }

    private void SaveSession(MainWindow? closing = null)
    {
        var windows = new List<SessionWindow>();

        foreach (MainWindow window in Application.Current.Windows.OfType<MainWindow>())
        {
            if (ReferenceEquals(window, closing)) continue;

            windows.Add(new SessionWindow
            {
                Urls = window._tabs
                    .Select(t => string.IsNullOrWhiteSpace(t.Source) ? Url.Home : t.Source)
                    .ToList(),
                Active = window._active is null ? 0 : Math.Max(0, window._tabs.IndexOf(window._active))
            });
        }

        _session.Save(windows);
    }

    private async Task<BrowserTab> OpenTabAsync(string? url, bool activate)
    {
        var tab = new BrowserTab { Home = _home, Visits = _visits, History = _history };
        _tabs.Add(tab);
        ContentHost.Children.Add(tab.View);

        Subscribe(tab);

        await tab.InitializeAsync(_environment!, url);

        if (activate) SelectTab(tab);
        else tab.IsActive = false;

        if (activate && HomePage.IsHome(url)) FocusAddress();

        return tab;
    }

    private void FocusAddress() => Dispatcher.BeginInvoke(
        DispatcherPriority.Input,
        new Action(() =>
        {
            AddressBar.Focus();
            AddressBar.SelectAll();
        }));

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

        bool nothingToShow = HomePage.IsHome(_active.Source) || BrowserTab.IsBlank(_active.Source);

        if (!AddressBar.IsKeyboardFocusWithin)
            AddressBar.Text = nothingToShow ? string.Empty : _active.Source;

        BlockCount.Text = $"{_active.Blocker.BlockedCount} blocked";
        Title = string.IsNullOrWhiteSpace(_active.Title) || nothingToShow
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

    private void UpdateMaximizedInset()
    {
        if (WindowState != System.Windows.WindowState.Maximized)
        {
            RootGrid.Margin = new Thickness(0);
            return;
        }

        int x = GetSystemMetrics(SystemMetricSizeFrameWidth) + GetSystemMetrics(SystemMetricPaddedBorder);
        int y = GetSystemMetrics(SystemMetricSizeFrameHeight) + GetSystemMetrics(SystemMetricPaddedBorder);

        RootGrid.Margin = new Thickness(x, y, x, y);
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
        ShowDefaultBar();
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
        if (!_isFirst) return;

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
        const int CornerPreferenceDoNotRound = 1;
        const int CornerPreferenceRound = 2;

        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        int preference = WindowState == WindowState.Maximized || _isFullScreen
            ? CornerPreferenceDoNotRound
            : CornerPreferenceRound;

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

    private const int SystemMetricSizeFrameWidth = 32;
    private const int SystemMetricSizeFrameHeight = 33;
    private const int SystemMetricPaddedBorder = 92;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

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

        BrowserTab dragged = _dragTab;
        bool detachable = _tabs.Count > 1;

        EndTabDrag(sender);

        if (detachable)
        {
            DetachTab(dragged, here);
            return;
        }

        RestoreBeforeDrag(here);

        DragAndMaybeMerge(dragged);
    }

    private void DetachTab(BrowserTab tab, Point inWindow)
    {
        int index = _tabs.IndexOf(tab);
        if (index < 0) return;

        Unsubscribe(tab);

        _tabs.Remove(tab);
        ContentHost.Children.Remove(tab.View);

        if (ReferenceEquals(_active, tab) && _tabs.Count > 0)
            SelectTab(_tabs[Math.Min(index, _tabs.Count - 1)]);

        var window = new MainWindow(_browser, isFirst: false);

        GetCursorPos(out NativePoint cursor);
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = cursor.x - inWindow.X;
        window.Top = cursor.y - inWindow.Y;

        window.Show();
        window.Adopt(tab);

        SaveSession();

        window.DragAndMaybeMerge(tab);
    }

    public void DragAndMaybeMerge(BrowserTab tab)
    {
        try { DragMove(); } catch (InvalidOperationException) { }

        if (StripUnderCursor(except: this) is not { } target) return;

        Unsubscribe(tab);

        _tabs.Remove(tab);
        ContentHost.Children.Remove(tab.View);

        target.Adopt(tab, target.DropIndex());
        target.Activate();

        if (_tabs.Count == 0) Close();
        else SaveSession();
    }

    private static MainWindow? StripUnderCursor(MainWindow except)
    {
        GetCursorPos(out NativePoint cursor);

        foreach (MainWindow window in Application.Current.Windows.OfType<MainWindow>())
        {
            if (ReferenceEquals(window, except)) continue;
            if (!window.IsVisible || window.WindowState == WindowState.Minimized) continue;

            try
            {
                Point topLeft = window.TabStripRow.PointToScreen(new Point(0, 0));
                Point bottomRight = window.TabStripRow.PointToScreen(
                    new Point(window.TabStripRow.ActualWidth, window.TabStripRow.ActualHeight));

                if (cursor.x >= topLeft.X && cursor.x <= bottomRight.X &&
                    cursor.y >= topLeft.Y && cursor.y <= bottomRight.Y)
                {
                    return window;
                }
            }
            catch (InvalidOperationException)
            {
            }
        }

        return null;
    }

    private int DropIndex()
    {
        GetCursorPos(out NativePoint cursor);

        for (int i = 0; i < _tabs.Count; i++)
        {
            if (TabStrip.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement slot) continue;

            try
            {
                Point topLeft = slot.PointToScreen(new Point(0, 0));

                if (cursor.x < topLeft.X + slot.ActualWidth / 2) return i;
            }
            catch (InvalidOperationException)
            {
            }
        }

        return _tabs.Count;
    }

    private void Subscribe(BrowserTab tab)
    {
        tab.Changed += OnTabChanged;
        tab.FullScreenChanged += OnFullScreenChanged;
        tab.NewWindowRequested += OnNewWindowRequested;
        tab.ContextMenuRequested += OnContextMenuRequested;
        tab.DownloadedIntoBlankTab += OnDownloadedIntoBlankTab;
        tab.Blocker.BlockedCountChanged += OnBlockedCountChanged;
    }

    private void Unsubscribe(BrowserTab tab)
    {
        tab.Changed -= OnTabChanged;
        tab.FullScreenChanged -= OnFullScreenChanged;
        tab.NewWindowRequested -= OnNewWindowRequested;
        tab.ContextMenuRequested -= OnContextMenuRequested;
        tab.DownloadedIntoBlankTab -= OnDownloadedIntoBlankTab;
        tab.Blocker.BlockedCountChanged -= OnBlockedCountChanged;
    }

    private void OnDownloadedIntoBlankTab(BrowserTab tab) => Dispatcher.BeginInvoke(() =>
    {
        if (!_tabs.Contains(tab) || !BrowserTab.IsBlank(tab.Source)) return;

        tab.View.CoreWebView2?.Navigate(Url.Home);
    });

    private void OnBlockedCountChanged(int count) => Dispatcher.BeginInvoke(UpdateChrome);

    private void Adopt(BrowserTab tab, int index = -1)
    {
        _tabs.Insert(index < 0 ? _tabs.Count : Math.Clamp(index, 0, _tabs.Count), tab);
        ContentHost.Children.Add(tab.View);

        Subscribe(tab);
        SelectTab(tab);
        SaveSession();
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

    private bool _completing;

    private bool _deleting;

    private void AddressBar_PreviewKeyDown(object sender, KeyEventArgs e) =>
        _deleting = e.Key is Key.Back or Key.Delete;

    private void AddressBar_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_completing || _deleting || !AddressBar.IsKeyboardFocused) return;

        string typed = AddressBar.Text;

        if (typed.Length == 0 || AddressBar.CaretIndex != typed.Length) return;

        if (Suggest(typed) is not { } match) return;

        _completing = true;
        AddressBar.Text = match;
        AddressBar.Select(typed.Length, match.Length - typed.Length);
        _completing = false;
    }

    private string? Suggest(string typed)
    {
        string prefix = typed.TrimStart();

        if (prefix.Length == 0 || prefix.Contains(' ') || prefix.Contains('/')) return null;

        foreach (string host in KnownHosts())
        {
            if (host.Length > prefix.Length &&
                host.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return host;
            }
        }

        return null;
    }

    private IEnumerable<string> KnownHosts()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Bookmark bookmark in _bookmarks.Items)
        {
            if (Bare(bookmark.Url) is { } host && seen.Add(host)) yield return host;
        }

        foreach (VisitedSite site in _visits.Top(int.MaxValue, Array.Empty<string>()))
        {
            string host = Strip(site.Host);
            if (seen.Add(host)) yield return host;
        }
    }

    private static string? Bare(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed) ? Strip(parsed.Host) : null;

    private static string Strip(string host) =>
        host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;

    private void AddressBar_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _active is null) return;

        _active.Navigate(AddressBar.Text);
        _active.View.Focus();
        e.Handled = true;
    }

    private void AddressBar_GotFocus(object sender, KeyboardFocusChangedEventArgs e) => AddressBar.SelectAll();
}
