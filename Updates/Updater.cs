using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;

namespace Lucent.Updates;

public sealed record UpdateInfo(Version Version, string AssetName, Uri Download, string Sha256)
{
    public string Display => Release.Format(Version);
}

public static class Updater
{
    public const string RetiredSuffix = ".old";

    private static readonly Regex Sha256Pattern = new(@"\b[A-Fa-f0-9]{64}\b", RegexOptions.Compiled);

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"Lucent/{Release.CurrentDisplay}");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using HttpClient http = CreateClient();
            string json = await http.GetStringAsync(Release.LatestApi, ct);

            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            if (!root.TryGetProperty("tag_name", out JsonElement tagElement)) return null;
            if (!Release.TryParseTag(tagElement.GetString(), out Version version)) return null;
            if (version <= Release.Current) return null;

            string body = root.TryGetProperty("body", out JsonElement bodyElement)
                ? bodyElement.GetString() ?? string.Empty
                : string.Empty;

            Match checksum = Sha256Pattern.Match(body);
            if (!checksum.Success) return null;

            if (!root.TryGetProperty("assets", out JsonElement assets)) return null;

            foreach (JsonElement asset in assets.EnumerateArray())
            {
                string? name = asset.TryGetProperty("name", out JsonElement n) ? n.GetString() : null;
                string? url = asset.TryGetProperty("browser_download_url", out JsonElement u) ? u.GetString() : null;

                if (name is null || url is null) continue;
                if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
                if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || !IsGitHub(uri)) continue;

                return new UpdateInfo(version, name, uri, checksum.Value.ToLowerInvariant());
            }

            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static async Task<string> DownloadAsync(
        UpdateInfo update, IProgress<double>? progress, CancellationToken ct = default)
    {
        string folder = Path.Combine(Path.GetTempPath(), "Lucent-update");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, update.AssetName);

        using (HttpClient http = CreateClient())
        using (HttpResponseMessage response =
               await http.GetAsync(update.Download, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();

            Uri served = response.RequestMessage?.RequestUri ?? update.Download;
            if (!IsGitHub(served))
                throw new InvalidOperationException($"Download was redirected off GitHub, to {served.Host}.");

            long? total = response.Content.Headers.ContentLength;

            await using Stream source = await response.Content.ReadAsStreamAsync(ct);
            await using var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[81920];
            long copied = 0;
            int read;

            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), ct);
                copied += read;
                if (total is > 0) progress?.Report((double)copied / total.Value);
            }
        }

        string actual = await Sha256Async(path, ct);
        if (!string.Equals(actual, update.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            TryDelete(path);
            throw new InvalidOperationException(
                "The download did not match the checksum in the release notes, so it was discarded.");
        }

        return path;
    }

    public static void ApplyAndRestart(string zipPath)
    {
        string current = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot locate the running executable.");

        string extracted = Path.Combine(Path.GetDirectoryName(zipPath)!, "unpacked");
        if (Directory.Exists(extracted)) Directory.Delete(extracted, recursive: true);
        ZipFile.ExtractToDirectory(zipPath, extracted);

        string replacement = Directory
            .EnumerateFiles(extracted, "Lucent.exe", SearchOption.AllDirectories)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("The downloaded archive does not contain Lucent.exe.");

        string retired = current + RetiredSuffix;
        TryDelete(retired);

        File.Move(current, retired);
        try
        {
            File.Copy(replacement, current);
        }
        catch
        {
            File.Move(retired, current);            throw;
        }

        SingleInstance.Release();

        Process.Start(new ProcessStartInfo(current) { UseShellExecute = true });
        Application.Current.Shutdown();
    }

    public static void RemoveRetired()
    {
        if (Environment.ProcessPath is not { } current) return;
        TryDelete(current + RetiredSuffix);
    }

    private static bool IsGitHub(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps &&
        (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase));

    private static async Task<string> Sha256Async(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        byte[] hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }
}
