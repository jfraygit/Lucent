namespace Lucent.Blocking;

public static class BlockList
{
    public static readonly string[] Domains =
    {
        "doubleclick.net",
        "googleadservices.com",
        "googlesyndication.com",
        "googletagservices.com",
        "googletagmanager.com",
        "google-analytics.com",
        "adservice.google.com",
        "2mdn.net",

        "amazon-adsystem.com",
        "spade.twitch.tv",
        "countess.twitch.tv",
        "ads.twitch.tv",

        "adnxs.com",
        "adsrvr.org",
        "criteo.com",
        "criteo.net",
        "rubiconproject.com",
        "pubmatic.com",
        "openx.net",
        "serving-sys.com",
        "moatads.com",
        "adsafeprotected.com",
        "taboola.com",
        "outbrain.com",

        "scorecardresearch.com",
        "quantserve.com",
        "bluekai.com",
        "demdex.net",
        "mixpanel.com",
        "hotjar.com",
        "fullstory.com",
        "segment.io",
    };

    public static readonly string[] UrlPatterns =
    {
        "https://*.youtube.com/pagead/*",
        "https://*.youtube.com/ptracking*",
        "https://*.youtube.com/api/stats/ads*",
        "https://*.youtube.com/get_midroll_*",
        "https://*.google.com/pagead/*",
    };

    private static readonly HashSet<string> DomainSet =
        new(Domains, StringComparer.OrdinalIgnoreCase);

    public static bool IsBlockedHost(string host)
    {
        if (string.IsNullOrEmpty(host)) return false;

        ReadOnlySpan<char> span = host.AsSpan();
        while (true)
        {
            if (DomainSet.Contains(span.ToString())) return true;

            int dot = span.IndexOf('.');
            if (dot < 0) return false;
            span = span[(dot + 1)..];
            if (span.IndexOf('.') < 0) return false;        }
    }
}
