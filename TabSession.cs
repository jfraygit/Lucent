using System.IO;
using System.Text.Json;

namespace Lucent;

public sealed class TabSession
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Lucent", "session.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private const int MaxTabs = 100;

    private sealed class SavedSession
    {
        public List<string> Urls { get; set; } = new();
        public int Active { get; set; }
    }

    public IReadOnlyList<string> Urls { get; private set; } = Array.Empty<string>();
    public int Active { get; private set; }

    public void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;

            SavedSession? saved = JsonSerializer.Deserialize<SavedSession>(File.ReadAllText(FilePath), Json);
            if (saved is null) return;

            List<string> urls = saved.Urls
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Take(MaxTabs)
                .ToList();

            Urls = urls;
            Active = Math.Clamp(saved.Active, 0, Math.Max(0, urls.Count - 1));
        }
        catch (Exception)
        {
        }
    }

    public void Save(IEnumerable<string> urls, int active)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

            var state = new SavedSession
            {
                Urls = urls.Where(u => !string.IsNullOrWhiteSpace(u)).Take(MaxTabs).ToList(),
                Active = active
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
