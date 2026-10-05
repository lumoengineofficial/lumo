using Lumo.Editor.Store;

namespace Lumo.Tests;

public class AssetStoreClientTests
{
    private const string SampleListJson = """
    {
      "items": [
        {
          "id": "asset-1",
          "title": "Low Poly Tree Pack",
          "slug": "low-poly-tree-pack",
          "description": "Ten hand-crafted low poly trees.",
          "author": { "id": "u1", "username": "Mira", "handle": "mira", "avatar": null },
          "category": "3D Models",
          "tags": ["tree", "lowpoly"],
          "price": 0,
          "downloads": 412,
          "thumbnail": "https://cdn.example.com/thumb.png",
          "version": "1.0.0",
          "file": "assets/abc/tree-pack.zip",
          "fileSize": 2411724,
          "license": "CC0",
          "rating": 4.7,
          "featured": true,
          "status": "approved"
        }
      ],
      "total": 1,
      "page": 1,
      "perPage": 12,
      "pageCount": 1
    }
    """;

    private const string SampleDownloadJson = """
    {
      "url": "https://cdn.example.com/files/tree-pack.zip",
      "filename": "tree-pack.zip",
      "fileSize": 2411724,
      "downloads": 412,
      "license": "CC0"
    }
    """;

    [Fact]
    public void StoreList_ParsesApiShape()
    {
        var result = System.Text.Json.JsonSerializer.Deserialize<StoreListResult>(SampleListJson,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(result);
        Assert.Equal(1, result.Total);
        Assert.Single(result.Items);
        var asset = result.Items[0];
        Assert.Equal("asset-1", asset.Id);
        Assert.Equal("Low Poly Tree Pack", asset.Title);
        Assert.Equal("mira", asset.Author?.Handle);
        Assert.Equal("3D Models", asset.Category);
        Assert.Equal(new[] { "tree", "lowpoly" }, asset.Tags);
        Assert.True(asset.IsFree);
        Assert.Equal(412, asset.Downloads);
        Assert.Equal(2411724, asset.FileSize);
        Assert.True(asset.Featured);
    }

    [Fact]
    public void StoreList_PaidAsset_IsNotFree()
    {
        string json = SampleListJson.Replace("\"price\": 0", "\"price\": 8.5");
        var opts = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var result = System.Text.Json.JsonSerializer.Deserialize<StoreListResult>(json, opts);

        Assert.False(result!.Items[0].IsFree);
        Assert.Equal(8.5, result.Items[0].Price);
    }

    [Fact]
    public void StoreDownloadInfo_ParsesResponse()
    {
        var info = System.Text.Json.JsonSerializer.Deserialize<StoreDownloadInfo>(SampleDownloadJson,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(info);
        Assert.Equal("https://cdn.example.com/files/tree-pack.zip", info.Url);
        Assert.Equal("tree-pack.zip", info.Filename);
        Assert.Equal(2411724, info.FileSize);
    }

    [Fact]
    public void NormalizeUrl_TrimsTrailingSlash()
    {
        Assert.Equal("https://lumoengine.vercel.app", StoreClient.NormalizeUrl("https://lumoengine.vercel.app/"));
        Assert.Equal(StoreClient.DefaultStoreUrl, StoreClient.NormalizeUrl("  "));
    }

    [Fact]
    public async Task GetListAsync_Live_ReturnsAssetsWhenOnline()
    {
        try
        {
            var result = await StoreClient.GetListAsync(StoreClient.DefaultStoreUrl);
            Assert.NotNull(result.Items);
            Assert.True(result.PageCount >= 1);
        }
        catch (HttpRequestException) { }
        catch (StoreApiException) { }
        catch (TaskCanceledException) { }
    }

    [Fact]
    public async Task GetDownloadInfoAsync_LivePaidWithoutSession_ReturnsLoginRequiredOrInfo()
    {
        try
        {
            var list = await StoreClient.GetListAsync(StoreClient.DefaultStoreUrl, perPage: 50);
            var paid = list.Items.Find(a => !a.IsFree);
            if (paid == null) return;

            var info = await StoreClient.GetDownloadInfoAsync(StoreClient.DefaultStoreUrl, paid.Id);
            Assert.False(string.IsNullOrEmpty(info.Url));
        }
        catch (StoreApiException ex)
        {
            Assert.Equal("login_required", ex.Code);
        }
        catch (HttpRequestException) { }
        catch (TaskCanceledException) { }
    }
}
