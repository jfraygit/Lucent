using System.Reflection;

namespace Lucent.Updates;

public static class Release
{
    public const string Owner = "jfraygit";

    public const string Repo = "Lucent";

    public static string LatestApi => $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
    public static string ReleasesPage => $"https://github.com/{Owner}/{Repo}/releases/latest";

    public const string WebView2Download = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    public static Version Current { get; } =
        Normalise(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0));

    public static string CurrentDisplay => Format(Current);

    public static Version Normalise(Version v) =>
        new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

    public static string Format(Version v) =>
        v.Revision > 0
            ? $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}"
            : $"{v.Major}.{v.Minor}.{v.Build}";

    public static bool TryParseTag(string? tag, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(tag)) return false;

        string text = tag.Trim();
        if (text.StartsWith('v') || text.StartsWith('V')) text = text[1..];

        if (!Version.TryParse(text, out Version? parsed)) return false;

        version = Normalise(parsed);
        return true;
    }
}
