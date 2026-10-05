using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Lumo.Engine.Core;

namespace Lumo.Editor.Store;

public sealed class StoreAuthor
{
    public string? Id { get; set; }
    public string? Username { get; set; }
    public string? Handle { get; set; }
    public string? Avatar { get; set; }
}

public sealed class StoreAsset
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? Description { get; set; }
    public StoreAuthor? Author { get; set; }
    public string Category { get; set; } = "";
    public string[] Tags { get; set; } = [];
    public double Price { get; set; }
    public int Downloads { get; set; }
    public string? Thumbnail { get; set; }
    public string Version { get; set; } = "1.0.0";
    public string? File { get; set; }
    public string? License { get; set; }
    public int FileSize { get; set; }
    public double Rating { get; set; }
    public bool Featured { get; set; }
    public string Status { get; set; } = "approved";

    public bool IsFree => Price <= 0;
}

public sealed class StoreListResult
{
    public List<StoreAsset> Items { get; set; } = [];
    public int Total { get; set; }
    public int Page { get; set; } = 1;
    public int PerPage { get; set; } = 12;
    public int PageCount { get; set; } = 1;
}

public sealed class StoreDownloadInfo
{
    public string Url { get; set; } = "";
    public string Filename { get; set; } = "";
    public int FileSize { get; set; }
    public int Downloads { get; set; }
    public string? License { get; set; }
}

public sealed class StoreApiException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>HTTP client for the Lumo Asset Store REST API (lumoengine.vercel.app).</summary>
public static class StoreClient
{
    /// <summary>The official store, used when no override is set in editor-settings.json.</summary>
    public const string BuiltinStoreUrl = "https://lumoengine.vercel.app";

    /// <summary>Active store base URL — honors the user's EditorSettings.StoreUrl override.</summary>
    public static string DefaultStoreUrl =>
        string.IsNullOrWhiteSpace(EditorSettings.StoreUrl)
            ? BuiltinStoreUrl
            : EditorSettings.StoreUrl.TrimEnd('/');

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public static string NormalizeUrl(string baseUrl)
        => string.IsNullOrWhiteSpace(baseUrl) ? DefaultStoreUrl : baseUrl.TrimEnd('/');

    public static async Task<StoreListResult> GetListAsync(string baseUrl, string? query = null,
        string? category = null, string? sort = null, int page = 1, int perPage = 12,
        CancellationToken ct = default)
    {
        var url = $"{NormalizeUrl(baseUrl)}/api/assets?page={page}&perPage={perPage}";
        if (!string.IsNullOrWhiteSpace(query)) url += $"&q={Uri.EscapeDataString(query)}";
        if (!string.IsNullOrWhiteSpace(category)) url += $"&category={Uri.EscapeDataString(category)}";
        if (!string.IsNullOrWhiteSpace(sort)) url += $"&sort={Uri.EscapeDataString(sort)}";

        using var resp = await Http.GetAsync(url, ct);
        string body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw await ToApiException(resp, body);

        var result = JsonSerializer.Deserialize<StoreListResult>(body, JsonOpts);
        return result ?? new StoreListResult();
    }

    public static async Task<StoreDownloadInfo> GetDownloadInfoAsync(string baseUrl, string assetId,
        CancellationToken ct = default)
    {
        var url = $"{NormalizeUrl(baseUrl)}/api/assets/{Uri.EscapeDataString(assetId)}/download?format=json";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.ParseAdd("application/json");

        using var resp = await Http.SendAsync(req, ct);
        string body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw await ToApiException(resp, body);

        var info = JsonSerializer.Deserialize<StoreDownloadInfo>(body, JsonOpts);
        if (info == null || info.Url.Length == 0)
            throw new StoreApiException("invalid_response", "Download response had no file URL.");
        return info;
    }

    public static async Task<byte[]> DownloadFileAsync(string fileUrl, CancellationToken ct = default)
        => await Http.GetByteArrayAsync(fileUrl, ct);

    private static async Task<StoreApiException> ToApiException(HttpResponseMessage resp, string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                string code = err.TryGetProperty("code", out var c) ? c.GetString() ?? "error" : "error";
                string message = err.TryGetProperty("message", out var m) ? m.GetString() ?? body : body;
                return new StoreApiException(code, message);
            }
        }
        catch (JsonException) { }
        return new StoreApiException($"http_{(int)resp.StatusCode}", $"Store request failed ({(int)resp.StatusCode}).");
    }
}
