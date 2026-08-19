using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Lucent.Bookmarks;

public sealed class Bookmark
{
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";

    public string? Icon { get; set; }

    private ImageSource? _image;
    private bool _decoded;

    [JsonIgnore]
    public ImageSource? Image
    {
        get
        {
            if (_decoded) return _image;
            _decoded = true;

            if (string.IsNullOrEmpty(Icon)) return null;

            try
            {
                using var stream = new MemoryStream(Convert.FromBase64String(Icon));
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                _image = bitmap;
            }
            catch (Exception)
            {
                _image = null;
            }

            return _image;
        }
    }
}

public sealed class BookmarkStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Lucent", "bookmarks.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public ObservableCollection<Bookmark> Items { get; } = new();

    public bool BarVisible { get; set; } = true;

    private sealed class SavedState
    {
        public bool BarVisible { get; set; } = true;
        public List<Bookmark> Items { get; set; } = new();
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;

            SavedState? state = JsonSerializer.Deserialize<SavedState>(File.ReadAllText(FilePath), Json);
            if (state is null) return;

            BarVisible = state.BarVisible;

            Items.Clear();
            foreach (Bookmark bookmark in state.Items)
            {
                if (!string.IsNullOrWhiteSpace(bookmark.Url)) Items.Add(bookmark);
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

            var state = new SavedState { BarVisible = BarVisible, Items = Items.ToList() };

            string temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(state, Json));
            File.Move(temporary, FilePath, overwrite: true);
        }
        catch (Exception)
        {
        }
    }

    public bool Contains(string? url) => Find(url) is not null;

    public Bookmark? Find(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        string key = Key(url);
        return Items.FirstOrDefault(b => Key(b.Url) == key);
    }

    public void Add(string url, string? title, ImageSource? icon)
    {
        if (string.IsNullOrWhiteSpace(url) || Contains(url)) return;

        Items.Add(new Bookmark
        {
            Url = url,
            Title = Label(url, title),
            Icon = Encode(icon)
        });

        Save();
    }

    public void Remove(string? url)
    {
        if (Find(url) is not { } existing) return;

        Items.Remove(existing);
        Save();
    }

    private static string Key(string url) => url.TrimEnd('/').ToLowerInvariant();

    private static string Label(string url, string? title)
    {
        if (!string.IsNullOrWhiteSpace(title)) return title.Trim();

        return Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed)
            ? parsed.Host.Replace("www.", string.Empty, StringComparison.OrdinalIgnoreCase)
            : url;
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
            return null;        }
    }
}
