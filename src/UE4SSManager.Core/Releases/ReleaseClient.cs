using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace UE4SSManager.Core.Releases;

public sealed class ReleaseFetchResult
{
    public required IReadOnlyList<Ue4ssRelease> Releases { get; init; }
    /// <summary>True when GitHub couldn't be reached and the list came from the local cache.</summary>
    public bool FromCache { get; init; }
    public string? Error { get; init; }
}

/// <summary>Talks to the GitHub releases API for RE-UE4SS and downloads release zips.</summary>
public sealed class ReleaseClient
{
    public const string DefaultRepository = "UE4SS-RE/RE-UE4SS";

    private readonly HttpClient _http;
    private readonly string _repository;
    private readonly string _cacheDir;

    public ReleaseClient(HttpClient http, string cacheDir, string repository = DefaultRepository, string? token = null)
    {
        _http = http;
        _repository = repository;
        _cacheDir = cacheDir;
        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
            _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("UE4SS-Manager", "0.1"));
        if (!string.IsNullOrWhiteSpace(token) && _http.DefaultRequestHeaders.Authorization is null)
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
    }

    private string CacheFile => Path.Combine(_cacheDir, "releases-" + _repository.Replace('/', '_') + ".json");

    public async Task<ReleaseFetchResult> GetReleasesAsync(CancellationToken ct = default)
    {
        string? error;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{_repository}/releases?per_page=50");
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
            {
                var releases = ParseReleases(json);
                TryWriteCache(json);
                return new ReleaseFetchResult { Releases = releases };
            }
            error = resp.StatusCode == HttpStatusCode.Forbidden || (int)resp.StatusCode == 429
                ? "GitHub rate limit reached (60 requests/hour without a token). Add a GitHub token in Options or try later."
                : $"GitHub answered {(int)resp.StatusCode} {resp.ReasonPhrase}.";
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            if (ct.IsCancellationRequested) throw;
            error = "Couldn't reach GitHub: " + e.Message;
        }

        var cached = TryReadCache();
        return new ReleaseFetchResult { Releases = cached ?? [], FromCache = cached is not null, Error = error };
    }

    public static IReadOnlyList<Ue4ssRelease> ParseReleases(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<Ue4ssRelease>();
        foreach (var r in doc.RootElement.EnumerateArray())
        {
            if (r.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True) continue;
            var assets = new List<ReleaseAsset>();
            if (r.TryGetProperty("assets", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in arr.EnumerateArray())
                {
                    assets.Add(new ReleaseAsset(
                        Str(a, "name") ?? "",
                        Str(a, "browser_download_url") ?? "",
                        a.TryGetProperty("size", out var s) && s.TryGetInt64(out var size) ? size : 0,
                        Date(a, "updated_at")));
                }
            }
            var tag = Str(r, "tag_name") ?? "";
            list.Add(new Ue4ssRelease(
                tag,
                Str(r, "name") is { Length: > 0 } n ? n : tag,
                r.TryGetProperty("prerelease", out var p) && p.ValueKind == JsonValueKind.True,
                Date(r, "published_at"),
                Str(r, "html_url"),
                Str(r, "body"),
                assets));
        }
        // Only releases that actually ship an installable UE4SS zip.
        return list.Where(r => r.AssetsOf(AssetKind.Standard).Any() || r.AssetsOf(AssetKind.Dev).Any())
                   .OrderByDescending(r => r.PublishedAt ?? DateTimeOffset.MinValue)
                   .ToList();
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static DateTimeOffset? Date(JsonElement e, string name) =>
        Str(e, name) is { } s && DateTimeOffset.TryParse(s, out var d) ? d : null;

    private void TryWriteCache(string json)
    {
        try
        {
            Directory.CreateDirectory(_cacheDir);
            File.WriteAllText(CacheFile, json);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private IReadOnlyList<Ue4ssRelease>? TryReadCache()
    {
        try { return File.Exists(CacheFile) ? ParseReleases(File.ReadAllText(CacheFile)) : null; }
        catch { return null; }
    }

    /// <summary>Downloads an asset into the download cache (re-using a complete earlier download) and returns the file path.</summary>
    public async Task<string> DownloadAsync(Ue4ssRelease release, ReleaseAsset asset, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var dir = Path.Combine(_cacheDir, "downloads", Util.PathUtil.SanitizeFileName(release.Tag));
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, Util.PathUtil.SanitizeFileName(asset.Name));
        if (File.Exists(target) && asset.Size > 0 && new FileInfo(target).Length == asset.Size && !release.IsExperimental)
        {
            progress?.Report(1);
            return target;
        }

        var tmp = target + ".part";
        using (var resp = await _http.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
        {
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? asset.Size;
            await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var dst = File.Create(tmp);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                done += read;
                if (total > 0) progress?.Report((double)done / total);
            }
        }
        File.Move(tmp, target, overwrite: true);
        progress?.Report(1);
        return target;
    }
}
