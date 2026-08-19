using System.IO;
using System.Reflection;
using System.Text.Json;
using Lucent.Home;
using Microsoft.Web.WebView2.Core;

namespace Lucent.Blocking;

public sealed class AdBlocker
{
    private sealed record Site(string Resource, string[] Hosts);

    private const string CoreResource = "Lucent.Scripts.core.js";
    private const string TwitchResource = "Lucent.Scripts.twitch.js";
    private const string TwitchWorkerResource = "Lucent.Scripts.twitch.worker.js";

    private const string TwitchWorkerBootstrap = "<twitch-worker-bootstrap>";

    private static readonly Site[] Sites =
    {
        new("Lucent.Scripts.youtube.js", new[] { "youtube.com", "youtube-nocookie.com", "youtu.be" }),
        new(TwitchResource,              new[] { "twitch.tv" }),
    };

    private static readonly string[] AllowedHosts =
        Sites.SelectMany(site => site.Hosts).Distinct().ToArray();

    private readonly string _namespace = "__" + Guid.NewGuid().ToString("N")[..12];

    private int _blocked;

    public int BlockedCount => _blocked;
    public event Action<int>? BlockedCountChanged;

    public async Task AttachAsync(CoreWebView2 core)
    {
        foreach (string domain in BlockList.Domains)
        {
            AddFilter(core, $"https://{domain}/*");
            AddFilter(core, $"https://*.{domain}/*");
            AddFilter(core, $"http://{domain}/*");
            AddFilter(core, $"http://*.{domain}/*");
        }

        foreach (string pattern in BlockList.UrlPatterns)
            AddFilter(core, pattern);

        core.WebResourceRequested += OnWebResourceRequested;

        await Install(core, CoreResource);
        await Install(core, TwitchWorkerBootstrap);

        foreach (Site site in Sites)
            await Install(core, site.Resource);
    }

    private Task<string> Install(CoreWebView2 core, string resource) =>
        core.AddScriptToExecuteOnDocumentCreatedAsync(SourceFor(resource));

    private string SourceFor(string resource)
    {
        string source = resource == TwitchWorkerBootstrap
            ? $"window.__LUCENT_NS__.workerSrc = {JsonSerializer.Serialize(ReadScript(TwitchWorkerResource))};"
            : ReadScript(resource);

        return source
            .Replace("__LUCENT_HOSTS__", JsonSerializer.Serialize(AllowedHosts), StringComparison.Ordinal)
            .Replace("__LUCENT_NS__", _namespace, StringComparison.Ordinal);
    }


    private static void AddFilter(CoreWebView2 core, string pattern)
    {
        try
        {
            core.AddWebResourceRequestedFilter(pattern, CoreWebView2WebResourceContext.All);
        }
        catch (ArgumentException)
        {
        }
    }

    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var core = (CoreWebView2)sender!;

        if (HomePage.IsHome(e.Request.Uri)) return;

        e.Response = core.Environment.CreateWebResourceResponse(
            null, 403, "Blocked by Lucent", "Access-Control-Allow-Origin: *");

        BlockedCountChanged?.Invoke(Interlocked.Increment(ref _blocked));
    }

    private static string ReadScript(string resourceName)
    {
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (stream is null)
            throw new InvalidOperationException(
                $"Embedded script '{resourceName}' is missing. Check the Scripts\\ folder is included as an EmbeddedResource.");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
