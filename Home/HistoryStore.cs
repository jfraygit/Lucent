using System.IO;
using System.Text.Json;

namespace Lucent.Home;

public sealed class HistoryEntry
{
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public string Host { get; set; } = "";

    public long At { get; set; }
}

public sealed class HistoryStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Lucent", "history.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private const int MaxEntries = 20000;

    private static readonly TimeSpan WriteEvery = TimeSpan.FromSeconds(20);

    private sealed class SavedHistory
    {
        public int RetentionDays { get; set; } = DefaultRetentionDays;
        public List<HistoryEntry> Entries { get; set; } = new();
    }

    public const int DefaultRetentionDays = 30;

    public int RetentionDays { get; private set; } = DefaultRetentionDays;

    private readonly List<HistoryEntry> _entries = new();
    private DateTime _lastWrite = DateTime.MinValue;
    private bool _dirty;

    public void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;

            SavedHistory? saved = JsonSerializer.Deserialize<SavedHistory>(File.ReadAllText(FilePath), Json);
            if (saved is null) return;

            RetentionDays = Sanitise(saved.RetentionDays);

            _entries.Clear();
            _entries.AddRange(saved.Entries.Where(e => !string.IsNullOrWhiteSpace(e.Url)));

            if (Prune()) Flush();
        }
        catch (Exception)
        {
        }
    }

    public IReadOnlyList<HistoryEntry> Entries =>
        _entries.OrderByDescending(e => e.At).ToList();

    public void Record(string? url, string? title)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed)) return;
        if (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp) return;
        if (HomePage.IsHome(url)) return;

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        HistoryEntry? last = _entries.Count > 0 ? _entries[^1] : null;
        if (last is not null && string.Equals(last.Url, url, StringComparison.OrdinalIgnoreCase))
        {
            last.At = now;
            if (!string.IsNullOrWhiteSpace(title)) last.Title = title.Trim();
            _dirty = true;
            Save();
            return;
        }

        _entries.Add(new HistoryEntry
        {
            Url = url!,
            Title = string.IsNullOrWhiteSpace(title) ? parsed.Host : title.Trim(),
            Host = parsed.Host,
            At = now
        });

        _dirty = true;
        Prune();
        Save();
    }

    public void SetRetention(int days)
    {
        RetentionDays = Sanitise(days);
        _dirty = true;

        Prune();
        Flush();
    }

    public void Clear()
    {
        _entries.Clear();
        _dirty = true;
        Flush();
    }

    public void Save()
    {
        if (!_dirty) return;
        if (DateTime.UtcNow - _lastWrite < WriteEvery) return;

        Flush();
    }

    public void Flush()
    {
        if (!_dirty) return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

            var state = new SavedHistory { RetentionDays = RetentionDays, Entries = _entries };

            string temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(state, Json));
            File.Move(temporary, FilePath, overwrite: true);

            _lastWrite = DateTime.UtcNow;
            _dirty = false;
        }
        catch (Exception)
        {
        }
    }

    private bool Prune()
    {
        int before = _entries.Count;

        if (RetentionDays > 0)
        {
            long cutoff = DateTimeOffset.UtcNow.AddDays(-RetentionDays).ToUnixTimeMilliseconds();
            _entries.RemoveAll(e => e.At < cutoff);
        }

        if (_entries.Count > MaxEntries)
            _entries.RemoveRange(0, _entries.Count - MaxEntries);

        if (_entries.Count == before) return false;

        _dirty = true;
        return true;
    }

    private static int Sanitise(int days) =>
        days is 0 or 1 or 7 or 30 or 90 ? days : DefaultRetentionDays;
}
