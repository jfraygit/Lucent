using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lucent.Blocking;
using Lucent.Home;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Lucent;

public sealed class BrowserTab : INotifyPropertyChanged
{
    public WebView2 View { get; } = new();
    public AdBlocker Blocker { get; } = new();

    private string _title = "New Tab";
    private bool _isActive;
    private ImageSource? _favicon;
    private bool _isDragging;
    private double _dragOffset;
    private double _width = 220;

    public bool IsDragging
    {
        get => _isDragging;
        set => Set(ref _isDragging, value);
    }

    public double DragOffset
    {
        get => _dragOffset;
        set => Set(ref _dragOffset, value);
    }

    public double Width
    {
        get => _width;
        set => Set(ref _width, value);
    }

    public string Title
    {
        get => _title;
        private set => Set(ref _title, value);
    }

    public ImageSource? Favicon
    {
        get => _favicon;
        private set => Set(ref _favicon, value);
    }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            Set(ref _isActive, value);

            View.Visibility = value ? System.Windows.Visibility.Visible
                                    : System.Windows.Visibility.Collapsed;
        }
    }

    public string Source => View.CoreWebView2?.Source ?? string.Empty;

    public static bool IsBlank(string? url) =>
        string.IsNullOrEmpty(url) || url.StartsWith("about:blank", StringComparison.OrdinalIgnoreCase);
    public bool CanGoBack => View.CoreWebView2?.CanGoBack ?? false;
    public bool CanGoForward => View.CoreWebView2?.CanGoForward ?? false;

    public event Action<BrowserTab>? Changed;
    public event Action<BrowserTab, CoreWebView2NewWindowRequestedEventArgs>? NewWindowRequested;
    public event Action<BrowserTab, bool>? FullScreenChanged;

    public event Action<BrowserTab, CoreWebView2ContextMenuRequestedEventArgs>? ContextMenuRequested;

    public event Action<BrowserTab>? DownloadedIntoBlankTab;

    public HomePage? Home { get; set; }

    public VisitStore? Visits { get; set; }

    public HistoryStore? History { get; set; }

    private static readonly System.Drawing.Color Unpainted =
        System.Drawing.Color.FromArgb(0xFF, 0x14, 0x14, 0x17);

    public async Task InitializeAsync(CoreWebView2Environment environment, string? navigateTo)
    {
        View.DefaultBackgroundColor = Unpainted;

        await View.EnsureCoreWebView2Async(environment);

        CoreWebView2 core = View.CoreWebView2;

        Harden(core);

        core.NavigationStarting += OnNavigationStartingSecurity;
        core.PermissionRequested += OnPermissionRequested;

        Home?.Attach(core);
        await Blocker.AttachAsync(core);

        core.NavigationCompleted += (_, e) =>
        {
            if (!e.IsSuccess) return;

            Visits?.Record(core.Source, core.DocumentTitle, Favicon);
            History?.Record(core.Source, core.DocumentTitle);
        };

        core.DocumentTitleChanged += (_, _) =>
        {
            Title = string.IsNullOrWhiteSpace(core.DocumentTitle) ? "New Tab" : core.DocumentTitle;

            History?.Record(core.Source, core.DocumentTitle);

            Changed?.Invoke(this);
        };
        core.FaviconChanged += async (_, _) => await UpdateFaviconAsync(core);
        core.SourceChanged += (_, _) => Changed?.Invoke(this);
        core.HistoryChanged += (_, _) => Changed?.Invoke(this);
        core.NewWindowRequested += (_, e) => NewWindowRequested?.Invoke(this, e);

        core.DownloadStarting += (_, _) =>
        {
            if (core.CanGoBack || !IsBlank(core.Source)) return;

            DownloadedIntoBlankTab?.Invoke(this);
        };

        core.ContextMenuRequested += (_, e) => ContextMenuRequested?.Invoke(this, e);
        core.ContainsFullScreenElementChanged += (_, _) =>
            FullScreenChanged?.Invoke(this, core.ContainsFullScreenElement);

        if (!string.IsNullOrWhiteSpace(navigateTo))
            core.Navigate(navigateTo);
    }

    private async Task UpdateFaviconAsync(CoreWebView2 core)
    {
        string requested = core.FaviconUri;

        if (string.IsNullOrEmpty(requested))
        {
            Favicon = null;
            return;
        }

        try
        {
            using Stream source = await core.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png);

            if (core.FaviconUri != requested) return;

            using var buffer = new MemoryStream();
            await source.CopyToAsync(buffer);
            buffer.Position = 0;

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = buffer;
            image.EndInit();
            image.Freeze();

            Favicon = image;
        }
        catch (Exception)
        {
            Favicon = null;
        }
    }

    private static void Harden(CoreWebView2 core)
    {
        CoreWebView2Settings settings = core.Settings;

        settings.IsPasswordAutosaveEnabled = false;
        settings.IsGeneralAutofillEnabled = false;

        settings.IsReputationCheckingRequired = true;

        settings.AreHostObjectsAllowed = false;
        settings.IsWebMessageEnabled = false;

        settings.IsStatusBarEnabled = false;
        settings.AreDevToolsEnabled = true;
        settings.AreDefaultContextMenusEnabled = true;
        settings.IsSwipeNavigationEnabled = false;
    }

    private static void OnPermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
    {
        e.State = e.PermissionKind == CoreWebView2PermissionKind.Autoplay
            ? CoreWebView2PermissionState.Allow
            : CoreWebView2PermissionState.Deny;

        e.Handled = true;
    }

    private static void OnNavigationStartingSecurity(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out Uri? target)) return;

        if (target.IsFile || target.IsUnc)
            e.Cancel = true;
    }

    public void Navigate(string input)
    {
        if (View.CoreWebView2 is null || string.IsNullOrWhiteSpace(input)) return;
        View.CoreWebView2.Navigate(Url.Normalize(input));
    }

    public void Dispose() => View.Dispose();

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
