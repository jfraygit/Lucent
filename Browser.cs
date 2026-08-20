using Lucent.Bookmarks;
using Lucent.Home;
using Lucent.Ui;
using Microsoft.Web.WebView2.Core;

namespace Lucent;

public sealed class Browser
{
    public BookmarkStore Bookmarks { get; } = new();
    public VisitStore Visits { get; } = new();
    public HistoryStore History { get; } = new();
    public WindowStateStore WindowState { get; } = new();
    public TabSession Session { get; } = new();
    public HomePage Home { get; }

    private Task<CoreWebView2Environment>? _environment;

    public bool SessionRestored { get; set; }

    public bool UpdateChecked { get; set; }

    public string? Pending { get; set; }

    public Browser()
    {
        Bookmarks.Load();
        Visits.Load();
        History.Load();
        WindowState.Load();
        Session.Load();

        Home = new HomePage(Bookmarks, Visits, History);
    }

    public Task<CoreWebView2Environment> EnvironmentAsync()
    {
        return _environment ??= CoreWebView2Environment.CreateAsync(
            null,
            App.UserDataFolder,
            new CoreWebView2EnvironmentOptions
            {
                AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required"
            });
    }
}
