using System.IO;
using System.Text.Json;

namespace Lucent;

public sealed class SessionWindow
{
    public List<string> Urls { get; set; } = new();
    public int Active { get; set; }
}

public sealed class TabSession
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Lucent", "session.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private const int MaxTabs = 100;

    private const int MaxWindows = 20;

    private sealed class SavedSession
    {
        public List<SessionWindow> Windows { get; set; } = new();

        public List<string>? Urls { get; set; }
        public int Active { get; set; }
    }

    public IReadOnlyList<SessionWindow> Windows { get; private set; } = Array.Empty<SessionWindow>();

    public void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;

            SavedSession? saved = JsonSerializer.Deserialize<SavedSession>(File.ReadAllText(FilePath), Json);
            if (saved is null) return;

            List<SessionWindow> windows = saved.Windows;

            if (windows.Count == 0 && saved.Urls is { Count: > 0 })
                windows = new List<SessionWindow> { new() { Urls = saved.Urls, Active = saved.Active } };

            Windows = windows
                .Select(w => new SessionWindow
                {
                    Urls = w.Urls.Where(u => !string.IsNullOrWhiteSpace(u)).Take(MaxTabs).ToList(),
                    Active = w.Active
                })
                .Where(w => w.Urls.Count > 0)
                .Take(MaxWindows)
                .ToList();
        }
        catch (Exception)
        {
        }
    }

    public void Save(IEnumerable<SessionWindow> windows)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

            var state = new SavedSession
            {
                Windows = windows
                    .Select(w => new SessionWindow
                    {
                        Urls = w.Urls.Where(u => !string.IsNullOrWhiteSpace(u)).Take(MaxTabs).ToList(),
                        Active = w.Active
                    })
                    .Where(w => w.Urls.Count > 0)
                    .Take(MaxWindows)
                    .ToList()
            };

            string temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(state, Json));
            File.Move(temporary, FilePath, overwrite: true);
        }
        catch (Exception)
        {
        }
    }
}
