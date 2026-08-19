using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows.Media.Imaging;
using Lucent.Bookmarks;
using Microsoft.Web.WebView2.Core;

namespace Lucent.Home;

public sealed class HomePage
{
    public const string Origin = "https://home.lucent";
    public const string Url = Origin + "/";

    private const string Resource = "Lucent.Scripts.home.html";

    private static readonly JsonSerializerOptions Wire = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private const int MostVisitedTiles = 12;

    private static readonly Lazy<string> Mark = new(() => LogoDataUri(256));
    private static readonly Lazy<string> Icon = new(() => LogoDataUri(48));

    private readonly BookmarkStore _bookmarks;
    private readonly VisitStore _visits;
    private readonly HistoryStore _history;

    public HomePage(BookmarkStore bookmarks, VisitStore visits, HistoryStore history)
    {
        _bookmarks = bookmarks;
        _visits = visits;
        _history = history;
    }

    public static bool IsHome(string? url) =>
        url is not null && url.StartsWith(Origin, StringComparison.OrdinalIgnoreCase);

    public void Attach(CoreWebView2 core)
    {
        core.AddWebResourceRequestedFilter($"{Origin}/*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += OnWebResourceRequested;
    }

    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (sender is not CoreWebView2 core) return;
        if (!IsHome(e.Request.Uri)) return;

        var requested = new Uri(e.Request.Uri);

        if (requested.AbsolutePath.Equals("/forget", StringComparison.OrdinalIgnoreCase))
        {
            Forget(requested.Query);

            e.Response = core.Environment.CreateWebResourceResponse(
                null, 302, "Found", $"Location: {Url}\r\nCache-Control: no-store");
            return;
        }

        if (requested.AbsolutePath.Equals("/history.json", StringComparison.OrdinalIgnoreCase))
        {
            Respond(core, e, History());
            return;
        }

        if (requested.AbsolutePath.Equals("/history/retention", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(Value(requested.Query, "days"), out int days)) _history.SetRetention(days);

            Respond(core, e, $"{{\"retentionDays\":{_history.RetentionDays}}}");
            return;
        }

        if (requested.AbsolutePath.Equals("/history/clear", StringComparison.OrdinalIgnoreCase))
        {
            _history.Clear();
            Respond(core, e, "{\"entries\":[],\"icons\":{}}");
            return;
        }

        byte[] body = Encoding.UTF8.GetBytes(Render());

        e.Response = core.Environment.CreateWebResourceResponse(
            new MemoryStream(body), 200, "OK",
            "Content-Type: text/html; charset=utf-8\r\n" +
            "Cache-Control: no-store\r\n" +
            "Content-Security-Policy: default-src 'none'; img-src data:; connect-src 'self'; " +
            "style-src 'unsafe-inline'; script-src 'unsafe-inline'; form-action https:");
    }

    private void Forget(string query)
    {
        string value = query.TrimStart('?');

        if (value.StartsWith("all=", StringComparison.OrdinalIgnoreCase))
        {
            _visits.Clear();
            return;
        }

        const string prefix = "host=";
        if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            _visits.Forget(Uri.UnescapeDataString(value[prefix.Length..]));
        }
    }

    private static void Respond(CoreWebView2 core, CoreWebView2WebResourceRequestedEventArgs e, string json)
    {
        byte[] body = Encoding.UTF8.GetBytes(json);

        e.Response = core.Environment.CreateWebResourceResponse(
            new MemoryStream(body), 200, "OK",
            "Content-Type: application/json; charset=utf-8\r\nCache-Control: no-store");
    }

    private static string? Value(string query, string key)
    {
        foreach (string pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int at = pair.IndexOf('=');
            if (at <= 0) continue;
            if (!pair[..at].Equals(key, StringComparison.OrdinalIgnoreCase)) continue;

            return Uri.UnescapeDataString(pair[(at + 1)..]);
        }

        return null;
    }

    private string History()
    {
        IReadOnlyList<HistoryEntry> entries = _history.Entries;

        var icons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (Bookmark bookmark in _bookmarks.Items)
        {
            if (bookmark.Icon is { } icon) icons[HostOf(bookmark.Url)] = icon;
        }

        foreach (VisitedSite site in _visits.Top(int.MaxValue, Array.Empty<string>()))
        {
            if (site.Icon is { } icon) icons.TryAdd(site.Host, icon);
        }

        var needed = new HashSet<string>(entries.Select(entry => entry.Host), StringComparer.OrdinalIgnoreCase);

        return JsonSerializer.Serialize(new
        {
            retentionDays = _history.RetentionDays,
            entries,
            icons = icons.Where(pair => needed.Contains(pair.Key))
                         .ToDictionary(pair => pair.Key, pair => pair.Value)
        }, Wire);
    }

    private string Render()
    {
        var bookmarkTiles = _bookmarks.Items
            .Select(b => new
            {
                host = HostOf(b.Url),
                url = b.Url,
                title = b.Title,
                icon = b.Icon
            })
            .ToList();

        var visitedTiles = _visits
            .Top(MostVisitedTiles, bookmarkTiles.Select(b => b.host))
            .Select(v => new
            {
                host = v.Host,
                url = v.Url,
                title = string.IsNullOrWhiteSpace(v.Title) ? v.Host : v.Title,
                icon = v.Icon
            })
            .ToList();

        string data = JsonSerializer.Serialize(new { bookmarks = bookmarkTiles, visited = visitedTiles });

        return Template()
            .Replace("__LUCENT_DATA__", data)
            .Replace("__LUCENT_MARK__", Mark.Value)
            .Replace("__LUCENT_ICON__", Icon.Value);
    }

    private static string LogoDataUri(int width)
    {
        try
        {
            var logo = new BitmapImage();
            logo.BeginInit();
            logo.UriSource = new Uri("pack://application:,,,/Assets/logo.png");
            logo.DecodePixelWidth = width;
            logo.CacheOption = BitmapCacheOption.OnLoad;
            logo.EndInit();

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(logo));

            using var buffer = new MemoryStream();
            encoder.Save(buffer);

            return "data:image/png;base64," + Convert.ToBase64String(buffer.ToArray());
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static string HostOf(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed) ? parsed.Host : url;

    private static string Template()
    {
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource);
        if (stream is null) return "<html><body>Home page resource missing.</body></html>";

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
