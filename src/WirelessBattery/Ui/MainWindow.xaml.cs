using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Media = System.Windows.Media;
using WpfButton = System.Windows.Controls.Button;
using System.Windows.Threading;
using WirelessBattery.Models;
using WirelessBattery.Polling;

namespace WirelessBattery.Ui;

public partial class MainWindow : Window
{
    private static readonly Media.SolidColorBrush OkBrush = Freeze(Media.Color.FromRgb(31, 107, 69));
    private static readonly Media.SolidColorBrush WarnBrush = Freeze(Media.Color.FromRgb(196, 123, 18));
    private static readonly Media.SolidColorBrush LowBrush = Freeze(Media.Color.FromRgb(194, 65, 45));
    private static readonly Media.SolidColorBrush MutedBrush = Freeze(Media.Color.FromRgb(138, 132, 120));
    private static readonly Media.SolidColorBrush IdleTextBrush = Freeze(Media.Color.FromRgb(63, 60, 55));
    private static readonly Media.SolidColorBrush IdleBorderBrush = Freeze(Media.Color.FromRgb(230, 226, 218));

    private readonly DispatcherTimer _ageTimer;
    private IReadOnlyList<DeviceBattery> _devices = [];
    private bool _exit;

    public MainWindow()
    {
        InitializeComponent();
        DeviceList.ItemsSource = Rows;
        _ageTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _ageTimer.Tick += (_, _) => Apply();
        _ageTimer.Start();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Hide();
        };
        PollSettings.Changed += OnPollSettingsChanged;
        HighlightInterval();
    }

    public ObservableCollection<DeviceRow> Rows { get; } = new();

    public Func<Task>? RefreshAsync { get; set; }

    public void Render(IReadOnlyList<DeviceBattery> devices)
    {
        _devices = devices;
        Apply();
        if (IsVisible)
            MoveNearTray();
    }

    public void ShowNearTray()
    {
        Apply();
        Topmost = true;
        Show();
        MoveNearTray();
        Activate();
    }

    private void MoveNearTray()
    {
        UpdateLayout();
        var area = SystemParameters.WorkArea;
        Left = area.Right - Math.Max(ActualWidth, Width) - 16;
        Top = Math.Max(area.Top + 16, area.Bottom - ActualHeight - 16);
    }
    public void Toggle()
    {
        if (IsVisible)
        {
            Topmost = false;
            Hide();
        }
        else
        {
            ShowNearTray();
        }
    }

    public void CloseForExit()
    {
        _exit = true;
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_exit)
        {
            e.Cancel = true;
            Topmost = false;
            Hide();
            return;
        }

        _ageTimer.Stop();
        PollSettings.Changed -= OnPollSettingsChanged;
        base.OnClosing(e);
    }

    private void OnIntervalClick(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { Tag: string text } && int.TryParse(text, out var minutes))
            PollSettings.SetMinutes(minutes);
    }

    private void OnPollSettingsChanged(object? sender, EventArgs e) => Dispatcher.Invoke(HighlightInterval);

    private void HighlightInterval()
    {
        foreach (var button in IntervalPanel.Children.OfType<WpfButton>())
        {
            var selected = button.Tag is string text
                && int.TryParse(text, out var minutes)
                && minutes == PollSettings.Minutes;
            button.Background = selected ? OkBrush : Media.Brushes.White;
            button.Foreground = selected ? Media.Brushes.White : IdleTextBrush;
            button.BorderBrush = selected ? OkBrush : IdleBorderBrush;
        }

        FootnoteText.Text = $"每 {PollSettings.Label(PollSettings.Minutes)}自动查询一次。立即刷新不受此限制。关闭窗口后仍在托盘运行。";
    }

    private void Apply()
    {
        var lowest = _devices.Where(device => device.CountsForTray).OrderBy(device => device.Percent).FirstOrDefault();
        SubtitleText.Text = _devices.Count == 0
            ? "正在读取设备…"
            : lowest?.Percent is int percent
                ? $"外设最低 {percent}% · {lowest.Name}"
                : "暂无外设电量";

        Rows.Clear();
        foreach (var device in _devices)
            Rows.Add(ToRow(device));

        EmptyText.Visibility = _devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = "正在读取设备…";
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        if (RefreshAsync is null)
            return;

        RefreshButton.IsEnabled = false;
        RefreshButton.Content = "正在刷新…";
        try
        {
            await RefreshAsync();
        }
        finally
        {
            RefreshButton.IsEnabled = true;
            RefreshButton.Content = "立即刷新";
        }
    }

    private void OnHideClick(object sender, RoutedEventArgs e)
    {
        Topmost = false;
        Hide();
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private static DeviceRow ToRow(DeviceBattery device)
    {
        var accent = Accent(device);
        return new DeviceRow
        {
            Name = device.Name,
            PercentText = device.Percent is int percent ? $"{percent}%" : "—",
            StatusText = device.StatusText,
            Meta = $"{device.Source} · {Age(device.UpdatedAt)}",
            Accent = accent
        };
    }

    private static Media.SolidColorBrush Accent(DeviceBattery device)
    {
        if (device.Kind != BatteryReadingKind.Level || device.IsStale || device.Percent is not int percent)
            return MutedBrush;
        if (percent <= 10)
            return LowBrush;
        if (percent <= 20)
            return WarnBrush;
        return OkBrush;
    }

    private static string Age(DateTimeOffset time)
    {
        var elapsed = DateTimeOffset.Now - time;
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;
        if (elapsed.TotalSeconds < 15)
            return "刚刚";
        if (elapsed.TotalMinutes < 1)
            return $"{Math.Max(1, (int)elapsed.TotalSeconds)} 秒前";
        if (elapsed.TotalHours < 1)
            return $"{(int)elapsed.TotalMinutes} 分钟前";
        return time.LocalDateTime.ToString("HH:mm");
    }

    private static Media.SolidColorBrush Freeze(Media.Color color)
    {
        var brush = new Media.SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

public sealed class DeviceRow
{
    public string Name { get; init; } = "";

    public string PercentText { get; init; } = "";

    public string StatusText { get; init; } = "";

    public string Meta { get; init; } = "";

    public Media.Brush Accent { get; init; } = Media.Brushes.Gray;
}
