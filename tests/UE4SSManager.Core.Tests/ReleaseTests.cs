using System.Net;
using UE4SSManager.Core.Releases;

namespace UE4SSManager.Core.Tests;

public class ReleaseTests
{
    // Shaped like api.github.com/repos/UE4SS-RE/RE-UE4SS/releases (trimmed).
    internal const string SampleJson = """
        [
          {
            "tag_name": "experimental-latest", "name": "experimental-latest", "draft": false, "prerelease": true,
            "published_at": "2025-06-01T10:00:00Z", "html_url": "https://github.com/UE4SS-RE/RE-UE4SS/releases/tag/experimental-latest",
            "body": "Rolling build",
            "assets": [
              { "name": "UE4SS_v3.0.1-596-g96c1d5d.zip", "browser_download_url": "https://example/exp.zip", "size": 100, "updated_at": "2025-06-01T10:00:00Z" },
              { "name": "zDEV-UE4SS_v3.0.1-596-g96c1d5d.zip", "browser_download_url": "https://example/expdev.zip", "size": 200 },
              { "name": "zCustomGameConfigs.zip", "browser_download_url": "https://example/cfg.zip", "size": 5 }
            ]
          },
          {
            "tag_name": "v3.0.1", "name": "v3.0.1 Beta", "draft": false, "prerelease": false, "published_at": "2024-03-01T10:00:00Z",
            "assets": [
              { "name": "UE4SS_v3.0.1.zip", "browser_download_url": "https://example/301.zip", "size": 100 },
              { "name": "zDEV-UE4SS_v3.0.1.zip", "browser_download_url": "https://example/301dev.zip", "size": 200 },
              { "name": "zMapGenBP.zip", "browser_download_url": "https://example/map.zip", "size": 5 }
            ]
          },
          {
            "tag_name": "v2.5.2", "name": "v2.5.2", "draft": false, "prerelease": false, "published_at": "2023-03-01T10:00:00Z",
            "assets": [
              { "name": "UE4SS_v2.5.2.zip", "browser_download_url": "https://example/252.zip", "size": 100 },
              { "name": "UE4SS_Xinput_v2.5.2.zip", "browser_download_url": "https://example/252x.zip", "size": 100 }
            ]
          },
          { "tag_name": "draft", "name": "draft", "draft": true, "prerelease": false, "assets": [] },
          { "tag_name": "docs", "name": "docs only", "draft": false, "prerelease": false, "published_at": "2022-01-01T00:00:00Z", "assets": [] }
        ]
        """;

    [Fact]
    public void Parses_and_classifies_releases()
    {
        var releases = ReleaseClient.ParseReleases(SampleJson);
        Assert.Equal(["experimental-latest", "v3.0.1", "v2.5.2"], releases.Select(r => r.Tag).ToArray());

        var exp = releases[0];
        Assert.True(exp.IsExperimental);
        Assert.Equal("3.0.1-596-g96c1d5d", exp.DisplayVersion);
        Assert.Equal("zDEV-UE4SS_v3.0.1-596-g96c1d5d.zip", exp.PickAsset(AssetKind.Dev)!.Name);

        var stable = releases[1];
        Assert.False(stable.IsExperimental);
        Assert.Equal("3.0.1", stable.DisplayVersion);
        Assert.Equal("UE4SS_v3.0.1.zip", stable.PickAsset(AssetKind.Standard)!.Name);
        Assert.Equal(AssetKind.Other, Ue4ssRelease.Classify("zMapGenBP.zip"));

        Assert.Equal("UE4SS_Xinput_v2.5.2.zip", releases[2].PickAsset(AssetKind.Standard)!.Name);
        Assert.False(releases[2].HasDevBuild);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(respond(request));
        }
    }

    [Fact]
    public async Task Falls_back_to_cache_when_github_is_unreachable()
    {
        using var t = new TempDir();
        var ok = new ReleaseClient(new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SampleJson) })), t.Path);
        var first = await ok.GetReleasesAsync();
        Assert.False(first.FromCache);
        Assert.Equal(3, first.Releases.Count);

        var down = new ReleaseClient(new HttpClient(new Handler(_ => throw new HttpRequestException("blocked"))), t.Path);
        var second = await down.GetReleasesAsync();
        Assert.True(second.FromCache);
        Assert.Equal(3, second.Releases.Count);
        Assert.Contains("blocked", second.Error);
    }

    [Fact]
    public async Task Rate_limit_gives_a_helpful_error()
    {
        using var t = new TempDir();
        var client = new ReleaseClient(new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("{}") })), t.Path);
        var result = await client.GetReleasesAsync();
        Assert.Empty(result.Releases);
        Assert.Contains("rate limit", result.Error);
    }

    [Fact]
    public async Task Download_writes_file_and_reuses_it()
    {
        using var t = new TempDir();
        var payload = new byte[100];
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });
        var client = new ReleaseClient(new HttpClient(handler), t.Path);
        var release = ReleaseClient.ParseReleases(SampleJson)[1];
        var asset = release.PickAsset(AssetKind.Standard)!;
        double last = 0;
        var path = await client.DownloadAsync(release, asset, new Progress<double>(p => last = p));
        Assert.Equal(100, new FileInfo(path).Length);
        await client.DownloadAsync(release, asset);
        Assert.Equal(1, handler.Calls);
    }
}
