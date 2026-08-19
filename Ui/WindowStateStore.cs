using System.IO;
using System.Text.Json;
using System.Windows;

namespace Lucent.Ui;

public sealed class WindowStateStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Lucent", "window.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private sealed class SavedState
    {
        public bool Maximized { get; set; }
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }

    public bool Maximized { get; set; }

    public Rect? Bounds { get; set; }

    public void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;

            SavedState? state = JsonSerializer.Deserialize<SavedState>(File.ReadAllText(FilePath), Json);
            if (state is null) return;

            Maximized = state.Maximized;

            if (state.Width > 0 && state.Height > 0)
                Bounds = new Rect(state.Left, state.Top, state.Width, state.Height);
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

            var state = new SavedState
            {
                Maximized = Maximized,
                Left = Bounds?.Left ?? 0,
                Top = Bounds?.Top ?? 0,
                Width = Bounds?.Width ?? 0,
                Height = Bounds?.Height ?? 0
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
