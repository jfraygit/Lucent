using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Lucent.Home;

public sealed class VisitedSite
{
    public string Host { get; set; } = "";
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public int Visits { get; set; }

    public string? Icon { get; set; }
}

public sealed class VisitStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Lucent", "sites.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private const int MaxSites = 60;

    private static readonly string[] PassingThrough =
    {
        "duckduckgo.com", "bing.com", "search.yahoo.com", "ecosia.org",
        "startpage.com", "search.brave.com", "yandex.com", "baidu.com",

        "t.co", "l.facebook.com", "lm.facebook.com", "l.instagram.com",
        "l.messenger.com", "out.reddit.com", "href.li", "away.vk.com"
    };

    private sealed class SavedState
    {
        public List<VisitedSite> Sites { get; set; } = new();

        public List<string> Hidden { get; set; } = new();
    }

    private readonly Dictionary<string, VisitedSite> _sites = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _hidden = new(StringComparer.OrdinalIgnoreCase);

    public void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;

            string text = File.ReadAllText(FilePath).TrimStart();
            if (text.Length == 0) return;

            if (text[0] == '[')
            {
                Adopt(JsonSerializer.Deserialize<List<VisitedSite>>(text, Json), Array.Empty<string>());
                return;
            }

            SavedState? saved = JsonSerializer.Deserialize<SavedState>(text, Json);
            if (saved is not null) Adopt(saved.Sites, saved.Hidden);
        }
        catch (Exception)
        {
        }
    }

    private void Adopt(List<VisitedSite>? sites, IEnumerable<string> hidden)
    {
        foreach (string host in hidden)
        {
            if (!string.IsNullOrWhiteSpace(host)) _hidden.Add(host);
        }

        bool dropped = false;

        foreach (VisitedSite site in sites ?? new List<VisitedSite>())
        {
            if (string.IsNullOrWhiteSpace(site.Host)) continue;

            if (Skip(site.Host))
            {
                dropped = true;
                continue;
            }

            _sites[site.Host] = site;
        }

        if (dropped) Save();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

            var state = new SavedState
            {
                Sites = _sites.Values.OrderByDescending(s => s.Visits).Take(MaxSites).ToList(),
                Hidden = _hidden.OrderBy(h => h).ToList()
            };

            string temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(state, Json));
            File.Move(temporary, FilePath, overwrite: true);
        }
        catch (Exception)
        {
        }
    }

    public void Record(string? url, string? title, ImageSource? icon)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed)) return;
        if (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp) return;
        if (HomePage.IsHome(url)) return;

        string host = parsed.Host;
        if (string.IsNullOrWhiteSpace(host) || Skip(host)) return;

        if (!_sites.TryGetValue(host, out VisitedSite? site))
        {
            site = new VisitedSite { Host = host, Url = $"{parsed.Scheme}://{host}/" };
            _sites[host] = site;
        }

        site.Visits++;

        if (!string.IsNullOrWhiteSpace(title)) site.Title = title.Trim();
        if (site.Icon is null && Encode(icon) is { } encoded) site.Icon = encoded;

        Save();
    }

    public IReadOnlyList<VisitedSite> Top(int count, IEnumerable<string> excludeHosts)
    {
        var skip = new HashSet<string>(excludeHosts, StringComparer.OrdinalIgnoreCase);

        return _sites.Values
            .Where(s => !skip.Contains(s.Host))
            .OrderByDescending(s => s.Visits)
            .ThenBy(s => s.Host)
            .Take(count)
            .ToList();
    }

    public void Forget(string host)
    {
        if (_sites.Remove(host)) Save();
    }

    public void Hide(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return;

        _hidden.Add(host);
        _sites.Remove(host);
        Save();
    }

    public void Clear()
    {
        _sites.Clear();
        Save();
    }

    private bool Skip(string host) => _hidden.Contains(host) || IsPassingThrough(host);

    private static bool IsPassingThrough(string host)
    {
        string bare = host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;

        if (bare.StartsWith("google.", StringComparison.OrdinalIgnoreCase)) return true;

        return PassingThrough.Contains(bare, StringComparer.OrdinalIgnoreCase);
    }

    private static string? Encode(ImageSource? icon)
    {
        if (icon is not BitmapSource source) return null;

        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));

            using var stream = new MemoryStream();
            encoder.Save(stream);
            return Convert.ToBase64String(stream.ToArray());
        }
        catch (Exception)
        {
            return null;
        }
    }
}
