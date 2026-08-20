namespace Lucent;

public static class Url
{
    public const string Home = Lucent.Home.HomePage.Url;

    private const string SearchTemplate = "https://duckduckgo.com/?q={0}";

    private static readonly string[] KnownSchemes =
        { "http://", "https://", "about:", "edge:", "devtools://" };

    public static string? FromLaunch(IReadOnlyList<string> arguments)
    {
        foreach (string argument in arguments)
        {
            string text = argument.Trim().Trim('"');

            if (text.Length == 0 || text[0] == '-' || text[0] == '/') continue;
            if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? parsed)) continue;

            if (parsed.Scheme is not ("http" or "https")) continue;

            return text;
        }

        return null;
    }

    public static string Normalize(string input)
    {
        string text = input.Trim();
        if (text.Length == 0) return Home;

        foreach (string scheme in KnownSchemes)
            if (text.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
                return text;

        bool looksLikeHost =
            !text.Contains(' ') &&
            (text.Contains('.') || text.StartsWith("localhost", StringComparison.OrdinalIgnoreCase));

        if (looksLikeHost && Uri.TryCreate("https://" + text, UriKind.Absolute, out _))
            return "https://" + text;

        return string.Format(SearchTemplate, Uri.EscapeDataString(text));
    }
}
