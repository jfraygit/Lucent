using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Lucent.Updates;
using Microsoft.Web.WebView2.Core;

namespace Lucent;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<BrowserTab> _tabs = new();

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

    public MainWindow()
    {
        InitializeComponent();

        NewTabCommand = new RelayCommand(() => _ = OpenTabAsync(Url.Home, activate: true));
        CloseTabCommand = new RelayCommand(() => { if (_active is not null) CloseTab(_active); });
        FocusAddressCommand = new RelayCommand(() => { AddressBar.Focus(); AddressBar.SelectAll(); });
        ReloadCommand = new RelayCommand(() => _active?.View.CoreWebView2?.Reload());

        TabStrip.ItemsSource = _tabs;
        Loaded += OnLoaded;
        StateChanged += (_, _) => UpdateContentInset();
        Closed += (_, _) => { foreach (BrowserTab tab in _tabs) tab.Dispose(); };
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyRoundedCorners();
        UpdateContentInset();
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

        await OpenTabAsync(Url.Home, activate: true);

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


    private async Task<BrowserTab> OpenTabAsync(string? url, bool activate)
    {
        var tab = new BrowserTab();
        _tabs.Add(tab);
        ContentHost.Children.Add(tab.View);

        tab.Changed += OnTabChanged;
        tab.FullScreenChanged += OnFullScreenChanged;
        tab.NewWindowRequested += OnNewWindowRequested;
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
    }

    private void OnTabChanged(BrowserTab tab)
    {
        if (ReferenceEquals(tab, _active)) UpdateChrome();
    }

    private async void OnNewWindowRequested(BrowserTab source, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        using CoreWebView2Deferral deferral = e.GetDeferral();

        BrowserTab tab = await OpenTabAsync(null, activate: true);
        e.NewWindow = tab.View.CoreWebView2;
    }


    private void UpdateChrome()
    {
        if (_active is null) return;

        BackButton.IsEnabled = _active.CanGoBack;
        ForwardButton.IsEnabled = _active.CanGoForward;

        if (!AddressBar.IsKeyboardFocusWithin)
            AddressBar.Text = _active.Source;

        BlockCount.Text = $"{_active.Blocker.BlockedCount} blocked";
        Title = string.IsNullOrWhiteSpace(_active.Title) ? "Lucent" : $"{_active.Title} - Lucent";
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
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WmGetMinMaxInfo = 0x0024;
        if (msg != WmGetMinMaxInfo) return IntPtr.Zero;

        IntPtr monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero) return IntPtr.Zero;

        var info = new NativeMonitorInfo { cbSize = Marshal.SizeOf<NativeMonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return IntPtr.Zero;

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
    private static extern bool GetMonitorInfo(IntPtr monitor, ref NativeMonitorInfo info);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);


    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();

    private void Tab_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is BrowserTab tab) SelectTab(tab);
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
