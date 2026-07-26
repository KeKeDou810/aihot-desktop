using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AIHotDesktop.Models;
using AIHotDesktop.Services;

namespace AIHotDesktop;

public partial class MainWindow : Window
{
    public static readonly DependencyProperty IsWidgetEngagedProperty =
        DependencyProperty.Register(
            nameof(IsWidgetEngaged),
            typeof(bool),
            typeof(MainWindow),
            new PropertyMetadata(false));

    private const int WmNcLeftButtonDown = 0x00A1;
    private static readonly IntPtr HitTestCaption = new(2);
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(15);
    private static readonly Brush OnlineBrush = BrushFrom("#829E73");
    private static readonly Brush CachedBrush = BrushFrom("#C7A15A");
    private static readonly Brush ErrorBrush = BrushFrom("#D87870");

    private readonly NewsService _newsService = new();
    private readonly AppSettingsStore _settingsStore = new();
    private readonly DispatcherTimer _refreshTimer = new();
    private readonly int? _qaItemCount;
    private AppSettings _settings = AppSettings.Default;
    private bool _isRefreshing;
    private bool _settingsUiReady;

    public bool IsWidgetEngaged
    {
        get => (bool)GetValue(IsWidgetEngagedProperty);
        private set => SetValue(IsWidgetEngagedProperty, value);
    }

    public MainWindow(int? qaItemCount = null)
    {
        _qaItemCount = qaItemCount;
        InitializeComponent();
        _refreshTimer.Tick += RefreshTimer_Tick;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = _settingsStore.Load();
        InitializeAppearanceControls();
        ApplyPosition(_settings);
        ConfigureResponsiveBounds();
        await RefreshAsync();
        RestartAutoRefreshTimer();
    }

    private void InitializeAppearanceControls()
    {
        QuietOpacitySlider.Value = _settings.QuietFrameOpacity * 100;
        HoverOpacitySlider.Value = _settings.HoverFrameOpacity * 100;
        HeaderOpacitySlider.Value =
            (_settings.HeaderQuietOpacity
                ?? AppSettings.DefaultHeaderQuietOpacity) * 100;
        CardOpacitySlider.Value =
            (_settings.CardOpacity
                ?? AppSettings.DefaultCardOpacity) * 100;
        _settingsUiReady = true;
        ApplyCardAppearance();
        UpdateAppearanceLabels();
        UpdateCardToneButtons();
        UpdateAppFrame(animate: false);
    }

    private void UpdateAppearanceLabels()
    {
        QuietOpacityText.Text =
            $"{Math.Round(_settings.QuietFrameOpacity * 100):0}%";
        HoverOpacityText.Text =
            $"{Math.Round(_settings.HoverFrameOpacity * 100):0}%";
        HeaderOpacityText.Text =
            $"{Math.Round(
                (_settings.HeaderQuietOpacity
                    ?? AppSettings.DefaultHeaderQuietOpacity) * 100):0}%";
        CardOpacityText.Text =
            $"{Math.Round(
                (_settings.CardOpacity
                    ?? AppSettings.DefaultCardOpacity) * 100):0}%";
        QuietPreview.Opacity = _settings.QuietFrameOpacity;
        HoverPreview.Opacity = _settings.HoverFrameOpacity;
        HeaderPreviewText.Opacity =
            _settings.HeaderQuietOpacity
                ?? AppSettings.DefaultHeaderQuietOpacity;
    }

    private void UpdateAppFrame(bool animate)
    {
        var isEngaged = WindowRoot.IsMouseOver || SettingsPopup.IsOpen;
        IsWidgetEngaged = isEngaged;
        var targetOpacity = isEngaged
            ? _settings.HoverFrameOpacity
            : _settings.QuietFrameOpacity;
        var targetHeaderOpacity = isEngaged
            ? 1
            : _settings.HeaderQuietOpacity
                ?? AppSettings.DefaultHeaderQuietOpacity;

        if (!animate)
        {
            AppFrame.BeginAnimation(OpacityProperty, null);
            AppFrame.Opacity = targetOpacity;
            HeaderInfoPanel.BeginAnimation(OpacityProperty, null);
            HeaderInfoPanel.Opacity = targetHeaderOpacity;
            return;
        }

        var duration = isEngaged
            ? TimeSpan.FromMilliseconds(120)
            : TimeSpan.FromMilliseconds(160);
        AppFrame.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(targetOpacity, duration)
            {
                EasingFunction = new QuadraticEase
                {
                    EasingMode = EasingMode.EaseOut
                }
            },
            HandoffBehavior.SnapshotAndReplace);
        HeaderInfoPanel.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(targetHeaderOpacity, duration)
            {
                EasingFunction = new QuadraticEase
                {
                    EasingMode = EasingMode.EaseOut
                }
            },
            HandoffBehavior.SnapshotAndReplace);
    }

    private void ApplyCardAppearance()
    {
        var palette = GetCardPalette(_settings.CardTone);
        var opacity =
            _settings.CardOpacity ?? AppSettings.DefaultCardOpacity;
        var background = CreateBrush(palette.Background, opacity);
        var hoverBackground = CreateBrush(
            palette.HoverBackground,
            Math.Min(1, opacity + 0.04));
        var pressedBackground = CreateBrush(
            palette.PressedBackground,
            Math.Min(1, opacity + 0.08));
        var border = CreateBrush(palette.Border, 0.28);

        Resources["CardBackgroundBrush"] = background;
        Resources["CardHoverBackgroundBrush"] = hoverBackground;
        Resources["CardPressedBackgroundBrush"] = pressedBackground;
        Resources["CardBorderBrush"] = border;
        CardPreview.Background = background;
        CardPreview.BorderBrush = border;
    }

    private void UpdateCardToneButtons()
    {
        var selectedTone =
            _settings.CardTone ?? AppSettings.DefaultCardTone;
        foreach (var button in new[]
                 {
                     CharcoalToneButton,
                     WarmToneButton,
                     PlumToneButton,
                     ForestToneButton
                 })
        {
            var isSelected = string.Equals(
                button.Tag as string,
                selectedTone,
                StringComparison.OrdinalIgnoreCase);
            button.BorderBrush = isSelected
                ? BrushFrom("#F0D4B8")
                : BrushFrom("#4DFFFFFF");
            button.BorderThickness = new Thickness(isSelected ? 2 : 1);
            button.Opacity = isSelected ? 1 : 0.68;
        }
    }

    private void ApplyPosition(AppSettings settings)
    {
        var workArea = SystemParameters.WorkArea;
        if (settings.Left is double savedLeft
            && settings.Top is double savedTop
            && IsVisiblePosition(savedLeft, savedTop))
        {
            Left = savedLeft;
            Top = savedTop;
            return;
        }

        Left = workArea.Right - Width - 34;
        Top = workArea.Top + 34;
    }

    private void ConfigureResponsiveBounds()
    {
        var workArea = SystemParameters.WorkArea;
        MaxHeight = Math.Max(240, workArea.Height - 48);

        UpdateLayout();
        var outerMargin = WindowRoot.Margin.Top + WindowRoot.Margin.Bottom;
        var chromeHeight =
            outerMargin + HeaderBar.ActualHeight + FooterBar.ActualHeight;
        NewsViewport.MaxHeight = Math.Max(96, MaxHeight - chromeHeight);
    }

    private bool IsVisiblePosition(double left, double top)
    {
        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        var virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
        var virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;
        var currentHeight = ActualHeight > 0 ? ActualHeight : 170;

        return left + 80 < virtualRight
            && top + 80 < virtualBottom
            && left + Width - 80 > virtualLeft
            && top + currentHeight - 80 > virtualTop;
    }

    private async Task RefreshAsync()
    {
        if (_isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        RefreshButton.IsEnabled = false;
        RefreshButton.Content = "…";
        StatusDot.Fill = CachedBrush;
        StatusText.Text = "正在检查更新";

        try
        {
            var result = await _newsService.LoadAsync();
            var sections = CreateQaSections(
                result.HotTopics,
                result.TodayNews,
                _qaItemCount);
            ShowItems(sections.HotTopics, sections.TodayNews);

            UpdatedText.Text =
                $"上次刷新：{result.CheckedAt.LocalDateTime:HH:mm}";

            var totalCount =
                sections.HotTopics.Count + sections.TodayNews.Count;
            if (totalCount == 0 && result.HasFailure)
            {
                StatusDot.Fill = ErrorBrush;
                StatusText.Text = "暂时离线";
            }
            else
            {
                StatusDot.Fill = result.IsStaleCache
                    ? CachedBrush
                    : OnlineBrush;
                StatusText.Text = totalCount == 0
                    ? "暂无更新"
                    : $"{totalCount} 条";
            }

            await ResizeAndConstrainAsync();
        }
        catch (Exception)
        {
            ShowItems([], []);
            EmptyStateTitle.Text = "暂时无法读取新闻";
            EmptyStateText.Text = "网络恢复后会自动再次检查";
            StatusDot.Fill = ErrorBrush;
            StatusText.Text = "检查失败";
            UpdatedText.Text = "上次刷新：--:--";
            await ResizeAndConstrainAsync();
        }
        finally
        {
            _isRefreshing = false;
            RefreshButton.IsEnabled = true;
            RefreshButton.Content = "↻";
        }
    }

    private void ShowItems(
        IReadOnlyList<NewsCardItem> hotTopics,
        IReadOnlyList<NewsCardItem> todayNews)
    {
        HotNewsList.ItemsSource = hotTopics;
        TodayNewsList.ItemsSource = todayNews;
        HotCountText.Text = $"{hotTopics.Count} 条";
        TodayCountText.Text = $"{todayNews.Count} 条";
        HotSection.Visibility = hotTopics.Count == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
        TodaySection.Visibility = todayNews.Count == 0
            ? Visibility.Collapsed
            : Visibility.Visible;

        var hasItems = hotTopics.Count + todayNews.Count > 0;
        NewsViewport.Visibility = hasItems
            ? Visibility.Visible
            : Visibility.Collapsed;
        EmptyState.Visibility = hasItems
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (!hasItems)
        {
            EmptyStateTitle.Text = "当前没有新的内容";
            EmptyStateText.Text = "AI HOT 暂未返回热点或今日精选";
        }
    }

    private async Task ResizeAndConstrainAsync()
    {
        await Dispatcher.InvokeAsync(
            () =>
            {
                UpdateLayout();
                ConstrainToVirtualScreen();
            },
            DispatcherPriority.Loaded);
    }

    private void ConstrainToVirtualScreen()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        var virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
        var virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;
        var maximumLeft = Math.Max(virtualLeft, virtualRight - ActualWidth);
        var maximumTop = Math.Max(virtualTop, virtualBottom - ActualHeight);

        Left = Math.Clamp(Left, virtualLeft, maximumLeft);
        Top = Math.Clamp(Top, virtualTop, maximumTop);
    }

    private static NewsSections CreateQaSections(
        IReadOnlyList<NewsCardItem> hotTopics,
        IReadOnlyList<NewsCardItem> todayNews,
        int? requestedCount)
    {
        if (requestedCount is null)
        {
            return new NewsSections(hotTopics, todayNews);
        }

        if (requestedCount == 0)
        {
            return new NewsSections([], []);
        }

        var hotCount = Math.Min(2, requestedCount.Value);
        var todayCount = requestedCount.Value - hotCount;
        return new NewsSections(
            CreateQaItems(hotTopics, hotCount, "hot"),
            CreateQaItems(todayNews, todayCount, "today"));
    }

    private static IReadOnlyList<NewsCardItem> CreateQaItems(
        IReadOnlyList<NewsCardItem> source,
        int requestedCount,
        string category)
    {
        if (requestedCount == 0)
        {
            return [];
        }

        IReadOnlyList<NewsCardItem> seeds = source.Count > 0
            ? source
            :
            [
                new NewsCardItem
                {
                    Id = $"qa-{category}-seed",
                    Rank = 1,
                    Title = category == "hot"
                        ? "用于验证热点分区的示例新闻"
                        : "用于验证今日新讯分区的示例新闻",
                    Summary = category == "hot"
                        ? "多个信源交叉关注"
                        : "今日精选内容摘要",
                    Source = "AI HOT QA",
                    Link = "https://aihot.virxact.com",
                    Category = category,
                    Timestamp = DateTimeOffset.Now
                }
            ];

        return Enumerable.Range(0, requestedCount)
            .Select(index =>
            {
                var seed = seeds[index % seeds.Count];
                return seed with
                {
                    Id = $"{seed.Id}-qa-{index + 1}",
                    Rank = index + 1,
                    Category = category,
                    Title = index < seeds.Count
                        ? seed.Title
                        : $"{seed.Title} · 布局验证 {index + 1}"
                };
            })
            .ToList();
    }

    private void RestartAutoRefreshTimer()
    {
        _refreshTimer.Stop();
        _refreshTimer.Interval = RefreshInterval;
        _refreshTimer.Start();
    }

    private async void RefreshTimer_Tick(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();
        await RefreshAsync();
        RestartAutoRefreshTimer();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
        RestartAutoRefreshTimer();
    }

    private async void RefreshMenuItem_Click(
        object sender,
        RoutedEventArgs e)
    {
        await RefreshAsync();
        RestartAutoRefreshTimer();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsPopup.IsOpen = true;
    }

    private void SettingsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        SettingsPopup.IsOpen = true;
    }

    private void CloseSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsPopup.IsOpen = false;
    }

    private void SettingsPopup_Opened(object? sender, EventArgs e)
    {
        UpdateAppFrame(animate: true);
    }

    private void SettingsPopup_Closed(object? sender, EventArgs e)
    {
        UpdateAppFrame(animate: true);
        SaveSettings();
    }

    private void OpacitySlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_settingsUiReady)
        {
            return;
        }

        _settings = _settings with
        {
            QuietFrameOpacity = QuietOpacitySlider.Value / 100,
            HoverFrameOpacity = HoverOpacitySlider.Value / 100,
            HeaderQuietOpacity = HeaderOpacitySlider.Value / 100,
            CardOpacity = CardOpacitySlider.Value / 100
        };
        ApplyCardAppearance();
        UpdateAppearanceLabels();
        UpdateAppFrame(animate: false);
        SaveSettings();
    }

    private void CardToneButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tone })
        {
            return;
        }

        _settings = _settings with { CardTone = tone };
        ApplyCardAppearance();
        UpdateCardToneButtons();
        SaveSettings();
    }

    private void ResetAppearanceButton_Click(object sender, RoutedEventArgs e)
    {
        QuietOpacitySlider.Value =
            AppSettings.DefaultQuietFrameOpacity * 100;
        HoverOpacitySlider.Value =
            AppSettings.DefaultHoverFrameOpacity * 100;
        HeaderOpacitySlider.Value =
            AppSettings.DefaultHeaderQuietOpacity * 100;
        CardOpacitySlider.Value =
            AppSettings.DefaultCardOpacity * 100;
        _settings = _settings with
        {
            CardTone = AppSettings.DefaultCardTone
        };
        ApplyCardAppearance();
        UpdateCardToneButtons();
        SaveSettings();
    }

    private void WindowRoot_MouseEnter(object sender, MouseEventArgs e)
    {
        UpdateAppFrame(animate: true);
    }

    private void WindowRoot_MouseLeave(object sender, MouseEventArgs e)
    {
        UpdateAppFrame(animate: true);
    }

    private void NewsItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: NewsCardItem item }
            || !Uri.TryCreate(item.Link, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.Host.Equals(
                "aihot.virxact.com",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Process.Start(new ProcessStartInfo(uri.AbsoluteUri)
        {
            UseShellExecute = true
        });
    }

    private void Window_PreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        var dragBoundary = WindowRoot.Margin.Top + HeaderBar.ActualHeight;
        if (e.ChangedButton != MouseButton.Left
            || e.ButtonState != MouseButtonState.Pressed
            || e.GetPosition(this).Y > dragBoundary
            || FindAncestor<Button>(
                e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        e.Handled = true;
        _ = ReleaseCapture();
        _ = SendMessage(
            new WindowInteropHelper(this).Handle,
            WmNcLeftButtonDown,
            HitTestCaption,
            IntPtr.Zero);
    }

    private static T? FindAncestor<T>(DependencyObject? source)
        where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match)
            {
                return match;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _refreshTimer.Stop();
        SaveSettings();
    }

    private void SaveSettings()
    {
        _settings = new AppSettings(
            Left,
            Top,
            _settings.QuietFrameOpacity,
            _settings.HoverFrameOpacity,
            _settings.HeaderQuietOpacity,
            _settings.CardOpacity,
            _settings.CardTone,
            AppSettings.CurrentVersion);
        _settingsStore.Save(_settings);
    }

    private static CardPalette GetCardPalette(string? tone)
    {
        return tone?.ToLowerInvariant() switch
        {
            "charcoal" => new CardPalette(
                "#171D24",
                "#1D252E",
                "#24303A",
                "#ABB8C4"),
            "plum" => new CardPalette(
                "#211B27",
                "#292130",
                "#32283A",
                "#BDA8C8"),
            "forest" => new CardPalette(
                "#18221D",
                "#1E2A24",
                "#25342C",
                "#9FB9A8"),
            _ => new CardPalette(
                "#241D1A",
                "#2C2420",
                "#362B26",
                "#C8AE96")
        };
    }

    private static Brush CreateBrush(string rgb, double opacity)
    {
        var color = (Color)ColorConverter.ConvertFromString(rgb);
        color.A = (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Brush BrushFrom(string color)
    {
        return (Brush)new BrushConverter().ConvertFromString(color)!;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(
        IntPtr windowHandle,
        int message,
        IntPtr wordParameter,
        IntPtr longParameter);

    private sealed record NewsSections(
        IReadOnlyList<NewsCardItem> HotTopics,
        IReadOnlyList<NewsCardItem> TodayNews);

    private sealed record CardPalette(
        string Background,
        string HoverBackground,
        string PressedBackground,
        string Border);
}
