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

    private readonly Dictionary<string, VisitedSite> _sites = new(StringComparer.OrdinalIgnoreCase);

    public void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;

            List<VisitedSite>? saved = JsonSerializer.Deserialize<List<VisitedSite>>(File.ReadAllText(FilePath), Json);
            if (saved is null) return;

            foreach (VisitedSite site in saved)
            {
                if (!string.IsNullOrWhiteSpace(site.Host)) _sites[site.Host] = site;
            }
        }
        catch (Exception)
        {
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

            List<VisitedSite> top = _sites.Values
                .OrderByDescending(s => s.Visits)
                .Take(MaxSites)
                .ToList();

            string temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(top, Json));
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
        if (string.IsNullOrWhiteSpace(host)) return;

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

    public void Clear()
    {
        _sites.Clear();
        Save();
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
