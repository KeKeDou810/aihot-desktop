using System.IO;
using System.Text.Json;

namespace AIHotDesktop.Services;

public sealed record AppSettings(
    double? Left,
    double? Top,
    double QuietFrameOpacity,
    double HoverFrameOpacity,
    double? HeaderQuietOpacity,
    double? CardOpacity,
    string? CardTone,
    int Version)
{
    public const int CurrentVersion = 6;
    public const double DefaultQuietFrameOpacity = 0.06;
    public const double DefaultHoverFrameOpacity = 0.34;
    public const double DefaultHeaderQuietOpacity = 0.86;
    public const double DefaultCardOpacity = 0.94;
    public const string DefaultCardTone = "warm";

    private static readonly HashSet<string> ValidCardTones =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "charcoal",
            "warm",
            "plum",
            "forest"
        };

    public static AppSettings Default { get; } =
        new(
            null,
            null,
            DefaultQuietFrameOpacity,
            DefaultHoverFrameOpacity,
            DefaultHeaderQuietOpacity,
            DefaultCardOpacity,
            DefaultCardTone,
            CurrentVersion);

    public AppSettings Normalize()
    {
        var quietOpacity = double.IsFinite(QuietFrameOpacity)
            ? Math.Clamp(QuietFrameOpacity, 0, 1)
            : DefaultQuietFrameOpacity;
        var hoverOpacity = double.IsFinite(HoverFrameOpacity)
            ? Math.Clamp(HoverFrameOpacity, 0, 1)
            : DefaultHoverFrameOpacity;
        var headerOpacity = HeaderQuietOpacity is double rawHeaderOpacity
            && double.IsFinite(rawHeaderOpacity)
                ? Math.Clamp(rawHeaderOpacity, 0.35, 1)
                : DefaultHeaderQuietOpacity;
        var cardOpacity = CardOpacity is double rawCardOpacity
            && double.IsFinite(rawCardOpacity)
                ? Math.Clamp(rawCardOpacity, 0.55, 1)
                : DefaultCardOpacity;
        var cardTone = CardTone is { Length: > 0 } rawCardTone
            && ValidCardTones.Contains(rawCardTone)
                ? rawCardTone.ToLowerInvariant()
                : DefaultCardTone;

        return this with
        {
            QuietFrameOpacity = quietOpacity,
            HoverFrameOpacity = hoverOpacity,
            HeaderQuietOpacity = headerOpacity,
            CardOpacity = cardOpacity,
            CardTone = cardTone,
            Version = CurrentVersion
        };
    }
}

public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;

    public AppSettingsStore()
    {
        var appDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AIHotDesktop");
        _settingsPath = Path.Combine(appDirectory, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return AppSettings.Default;
            }

            var json = File.ReadAllText(_settingsPath);
            var settings =
                JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                ?? AppSettings.Default;

            return settings.Normalize();
        }
        catch (Exception)
        {
            return AppSettings.Default;
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            var directory = Path.GetDirectoryName(_settingsPath)!;
            Directory.CreateDirectory(directory);

            var temporaryPath = _settingsPath + ".tmp";
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(settings.Normalize(), JsonOptions));
            File.Move(temporaryPath, _settingsPath, overwrite: true);
        }
        catch (Exception)
        {
            // Appearance preferences must never block app shutdown.
        }
    }
}
