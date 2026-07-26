using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIHotDesktop.Models;

namespace AIHotDesktop.Services;

public sealed class NewsService
{
    private const string HotTopicsUrl =
        "https://aihot.virxact.com/api/v1/hot-topics";
    private const string TodayItemsUrl =
        "https://aihot.virxact.com/api/v1/items?mode=selected&window=24h&by=timeline&limit=100";

    private static readonly TimeZoneInfo ChinaTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
    private static readonly HttpClient Client = CreateClient();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _hotTopicsCachePath;
    private readonly string _todayItemsCachePath;

    public NewsService()
    {
        var appDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AIHotDesktop");
        _hotTopicsCachePath = Path.Combine(
            appDirectory,
            "hot-topics-cache-v1.json");
        _todayItemsCachePath = Path.Combine(
            appDirectory,
            "today-selected-cache-v1.json");
    }

    public async Task<NewsLoadResult> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        var hotTopicsTask = LoadSectionAsync<ApiHotTopicsResponse>(
            HotTopicsUrl,
            _hotTopicsCachePath,
            MapHotTopics,
            cancellationToken);
        var todayItemsTask = LoadSectionAsync<ApiItemsResponse>(
            TodayItemsUrl,
            _todayItemsCachePath,
            MapTodayItems,
            cancellationToken);

        await Task.WhenAll(hotTopicsTask, todayItemsTask);

        var hotTopics = await hotTopicsTask;
        var todayItems = await todayItemsTask;
        var currentTodayItems = KeepCurrentShanghaiDay(todayItems.Items);
        var checkedAt = hotTopics.RequestSucceeded || todayItems.RequestSucceeded
            ? DateTimeOffset.Now
            : LatestCacheTime(hotTopics.FetchedAt, todayItems.FetchedAt);

        return new NewsLoadResult(
            hotTopics.Items,
            currentTodayItems,
            checkedAt,
            IsStaleCache: hotTopics.IsStaleCache || todayItems.IsStaleCache,
            HasFailure: hotTopics.HasFailure || todayItems.HasFailure);
    }

    private static async Task<SectionLoadResult> LoadSectionAsync<TPayload>(
        string url,
        string cachePath,
        Func<TPayload, List<NewsCardItem>> mapItems,
        CancellationToken cancellationToken)
        where TPayload : class
    {
        var cache = await LoadCacheAsync(cachePath, cancellationToken);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.UserAgent.ParseAdd(
                "AIHotDesktop/0.1.0");

            if (!string.IsNullOrWhiteSpace(cache?.ETag)
                && EntityTagHeaderValue.TryParse(cache.ETag, out var etag))
            {
                request.Headers.IfNoneMatch.Add(etag);
            }

            using var response = await Client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotModified
                && cache is not null)
            {
                return new SectionLoadResult(
                    cache.Items,
                    cache.FetchedAt,
                    IsStaleCache: false,
                    HasFailure: false,
                    RequestSucceeded: true);
            }

            response.EnsureSuccessStatusCode();

            await using var stream =
                await response.Content.ReadAsStreamAsync(cancellationToken);
            var payload = await JsonSerializer.DeserializeAsync<TPayload>(
                stream,
                JsonOptions,
                cancellationToken);
            var items = payload is null ? [] : mapItems(payload);
            var fetchedAt = DateTimeOffset.Now;
            var newCache = new NewsCache(
                response.Headers.ETag?.ToString(),
                fetchedAt,
                items);

            await SaveCacheAsync(cachePath, newCache, cancellationToken);

            return new SectionLoadResult(
                items,
                fetchedAt,
                IsStaleCache: false,
                HasFailure: false,
                RequestSucceeded: true);
        }
        catch (Exception) when (cache is not null)
        {
            return new SectionLoadResult(
                cache.Items,
                cache.FetchedAt,
                IsStaleCache: true,
                HasFailure: true,
                RequestSucceeded: false);
        }
        catch (Exception)
        {
            return new SectionLoadResult(
                [],
                DateTimeOffset.MinValue,
                IsStaleCache: false,
                HasFailure: true,
                RequestSucceeded: false);
        }
    }

    private static List<NewsCardItem> MapHotTopics(
        ApiHotTopicsResponse payload)
    {
        return payload.Items
            .Where(item =>
                !string.IsNullOrWhiteSpace(item.Id)
                && !string.IsNullOrWhiteSpace(item.Title)
                && Uri.TryCreate(item.Links.Aihot, UriKind.Absolute, out _))
            .Select((item, index) => new NewsCardItem
            {
                Id = item.Id,
                Rank = index + 1,
                Title = Normalize(item.Title),
                Summary = item.SourceCount switch
                {
                    0 => "多源热度持续变化",
                    1 => "1 个信源正在关注",
                    _ => $"{item.SourceCount} 个信源交叉关注"
                },
                Source = Normalize(item.Source.Name) is { Length: > 0 } source
                    ? source
                    : "AI HOT",
                Link = item.Links.Aihot,
                Category = "hot",
                Timestamp = item.LatestAt
            })
            .ToList();
    }

    private static List<NewsCardItem> MapTodayItems(ApiItemsResponse payload)
    {
        var nowInChina = TimeZoneInfo.ConvertTime(
            DateTimeOffset.Now,
            ChinaTimeZone);

        return payload.Items
            .Where(item =>
                !string.IsNullOrWhiteSpace(item.Id)
                && !string.IsNullOrWhiteSpace(item.Title)
                && Uri.TryCreate(item.Links.Aihot, UriKind.Absolute, out _))
            .Select(item => new
            {
                Item = item,
                TimelineAt = GetTimelineAt(item)
            })
            .Select(entry => new
            {
                entry.Item,
                TimelineAt = TimeZoneInfo.ConvertTime(
                    entry.TimelineAt,
                    ChinaTimeZone)
            })
            .Where(entry => entry.TimelineAt.Date == nowInChina.Date)
            .OrderByDescending(entry => entry.TimelineAt)
            .Select((entry, index) => new NewsCardItem
            {
                Id = entry.Item.Id,
                Rank = index + 1,
                Title = Normalize(entry.Item.Title),
                Summary = Normalize(entry.Item.Summary) is { Length: > 0 } summary
                    ? summary
                    : "打开 AI HOT 查看详情",
                Source = Normalize(entry.Item.Source.Name) is { Length: > 0 } source
                    ? source
                    : "AI HOT",
                Link = entry.Item.Links.Aihot,
                Category = "today",
                Timestamp = entry.TimelineAt
            })
            .ToList();
    }

    private static DateTimeOffset GetTimelineAt(ApiSelectedItem item)
    {
        return item.DiscoveredAt - item.PublishedAt > TimeSpan.FromHours(72)
            ? item.PublishedAt
            : item.DiscoveredAt;
    }

    private static List<NewsCardItem> KeepCurrentShanghaiDay(
        IReadOnlyList<NewsCardItem> items)
    {
        var today = TimeZoneInfo.ConvertTime(
            DateTimeOffset.Now,
            ChinaTimeZone).Date;

        return items
            .Where(item =>
                TimeZoneInfo.ConvertTime(item.Timestamp, ChinaTimeZone).Date
                == today)
            .OrderByDescending(item => item.Timestamp)
            .Select((item, index) => item with { Rank = index + 1 })
            .ToList();
    }

    private static DateTimeOffset LatestCacheTime(
        DateTimeOffset first,
        DateTimeOffset second)
    {
        var latest = first > second ? first : second;
        return latest == DateTimeOffset.MinValue
            ? DateTimeOffset.Now
            : latest;
    }

    private static async Task<NewsCache?> LoadCacheAsync(
        string cachePath,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(cachePath))
            {
                return null;
            }

            await using var stream = File.OpenRead(cachePath);
            return await JsonSerializer.DeserializeAsync<NewsCache>(
                stream,
                JsonOptions,
                cancellationToken);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static async Task SaveCacheAsync(
        string cachePath,
        NewsCache cache,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(cachePath)!;
        Directory.CreateDirectory(directory);

        var temporaryPath = cachePath + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                cache,
                JsonOptions,
                cancellationToken);
        }

        File.Move(temporaryPath, cachePath, overwrite: true);
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(
            " ",
            value.Split(
                [' ', '\r', '\n', '\t'],
                StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries));
    }

    private static HttpClient CreateClient()
    {
        return new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    private sealed record ApiHotTopicsResponse(
        [property: JsonPropertyName("items")] List<ApiHotTopic> Items);

    private sealed record ApiHotTopic(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("source")] ApiSource Source,
        [property: JsonPropertyName("links")] ApiLinks Links,
        [property: JsonPropertyName("sourceCount")] int SourceCount,
        [property: JsonPropertyName("latestAt")] DateTimeOffset LatestAt);

    private sealed record ApiItemsResponse(
        [property: JsonPropertyName("items")] List<ApiSelectedItem> Items);

    private sealed record ApiSelectedItem(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("summary")] string? Summary,
        [property: JsonPropertyName("source")] ApiSource Source,
        [property: JsonPropertyName("links")] ApiLinks Links,
        [property: JsonPropertyName("publishedAt")] DateTimeOffset PublishedAt,
        [property: JsonPropertyName("discoveredAt")] DateTimeOffset DiscoveredAt);

    private sealed record ApiSource(
        [property: JsonPropertyName("name")] string? Name);

    private sealed record ApiLinks(
        [property: JsonPropertyName("aihot")] string Aihot,
        [property: JsonPropertyName("original")] string? Original);

    private sealed record NewsCache(
        string? ETag,
        DateTimeOffset FetchedAt,
        List<NewsCardItem> Items);

    private sealed record SectionLoadResult(
        IReadOnlyList<NewsCardItem> Items,
        DateTimeOffset FetchedAt,
        bool IsStaleCache,
        bool HasFailure,
        bool RequestSucceeded);
}

public sealed record NewsLoadResult(
    IReadOnlyList<NewsCardItem> HotTopics,
    IReadOnlyList<NewsCardItem> TodayNews,
    DateTimeOffset CheckedAt,
    bool IsStaleCache,
    bool HasFailure);
