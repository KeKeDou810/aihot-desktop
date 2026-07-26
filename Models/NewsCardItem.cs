using System.Globalization;
using System.Text.Json.Serialization;

namespace AIHotDesktop.Models;

public sealed record NewsCardItem
{
    public required string Id { get; init; }
    public required int Rank { get; init; }
    public required string Title { get; init; }
    public required string Summary { get; init; }
    public required string Source { get; init; }
    public required string Link { get; init; }
    public required string Category { get; init; }
    public required DateTimeOffset Timestamp { get; init; }

    [JsonIgnore]
    public string LeadText => Category == "today"
        ? Timestamp.ToString("HH:mm", CultureInfo.InvariantCulture)
        : Rank.ToString(CultureInfo.InvariantCulture);

    [JsonIgnore]
    public string CategoryLabel => Category switch
    {
        "ai-models" => "AI 模型",
        "ai-products" => "AI 产品",
        "industry" => "行业动态",
        "paper" => "论文",
        "tip" => "技巧观点",
        "hot" => "当前热点",
        "today" => "今日新讯",
        _ => "AI 资讯"
    };

    [JsonIgnore]
    public string AccentColor => Category switch
    {
        "hot" => "#D87870",
        "today" => "#C7A15A",
        "ai-models" => "#B692F6",
        "ai-products" => "#D87870",
        "industry" => "#C7A15A",
        "paper" => "#B692F6",
        "tip" => "#829E73",
        _ => "#9AA7B3"
    };

    [JsonIgnore]
    public string TimeLabel
    {
        get
        {
            var elapsed = DateTimeOffset.Now - Timestamp;

            if (elapsed < TimeSpan.FromMinutes(1))
            {
                return "刚刚";
            }

            if (elapsed < TimeSpan.FromHours(1))
            {
                return $"{Math.Max(1, (int)elapsed.TotalMinutes)} 分钟前";
            }

            if (elapsed < TimeSpan.FromDays(1))
            {
                return $"{Math.Max(1, (int)elapsed.TotalHours)} 小时前";
            }

            if (elapsed < TimeSpan.FromDays(2))
            {
                return "昨天";
            }

            return Timestamp.ToString("MM/dd", CultureInfo.InvariantCulture);
        }
    }
}
