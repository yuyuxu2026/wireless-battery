using System.Drawing;
using System.Windows.Forms;
using WirelessBattery.Models;
using WirelessBattery.Polling;
using WirelessBattery.Providers;
using Application = System.Windows.Application;

namespace WirelessBattery.Ui;

public sealed class TrayController : IDisposable
{
    private readonly BatteryPoller _poller;
    private readonly MainWindow _window;
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _startupItem;
    private readonly ToolStripMenuItem _trayDisplayItem;
    private readonly ToolStripMenuItem _intervalMenu;
    private Icon? _currentIcon;
    private bool _disposed;

    public TrayController()
    {
        var tracker = new LowBatteryTracker();
        _poller = new BatteryPoller(
            [
                new LaptopBatteryProvider(),
                new LogitechHidppProvider(),
                new BleBatteryProvider(),
                new RogKeyboardProvider()
            ],
            tracker);

        _window = new MainWindow
        {
            RefreshAsync = () => _poller.RefreshNowAsync(),
            RequestExit = Exit
        };

        _startupItem = new ToolStripMenuItem("开机启动")
        {
            CheckOnClick = true
        };
        var menu = new ContextMenuStrip();
        var refresh = new ToolStripMenuItem("立即刷新");
        refresh.Click += async (_, _) =>
        {
            try
            {
                await _poller.RefreshNowAsync();
            }
            catch (ObjectDisposedException)
            {
            }
        };
        _startupItem.Click += (_, _) => StartupRegistration.SetEnabled(_startupItem.Checked);
        _trayDisplayItem = new ToolStripMenuItem("在托盘显示")
        {
            CheckOnClick = true,
            Checked = TrayDisplaySettings.ShowInTray
        };
        _trayDisplayItem.Click += (_, _) => TrayDisplaySettings.SetEnabled(_trayDisplayItem.Checked);
        _intervalMenu = new ToolStripMenuItem("查询间隔");
        foreach (var minutes in PollSettings.Choices)
        {
            var chosen = minutes;
            var item = new ToolStripMenuItem(PollSettings.Label(chosen))
            {
                Tag = chosen
            };
            item.Click += (_, _) => PollSettings.SetMinutes(chosen);
            _intervalMenu.DropDownItems.Add(item);
        }
        var exit = new ToolStripMenuItem("退出");
        exit.Click += (_, _) => Exit();
        menu.Opening += (_, _) =>
        {
            _startupItem.Checked = StartupRegistration.IsEnabled();
            _trayDisplayItem.Checked = TrayDisplaySettings.ShowInTray;
            foreach (ToolStripMenuItem item in _intervalMenu.DropDownItems)
                item.Checked = item.Tag is int minutes && minutes == PollSettings.Minutes;
        };
        menu.Items.Add(refresh);
        menu.Items.Add(_intervalMenu);
        menu.Items.Add(_startupItem);
        menu.Items.Add(_trayDisplayItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exit);

        _currentIcon = TrayIconPainter.Create(null);
        _icon = new NotifyIcon
        {
            Icon = _currentIcon,
            Text = "无线设备电量",
            Visible = TrayDisplaySettings.ShowInTray,
            ContextMenuStrip = menu
        };
        _icon.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                _window.Toggle();
        };

        _poller.Updated += (_, _) => Application.Current.Dispatcher.Invoke(ApplySnapshot);
        _poller.Alerts += (_, alerts) => Application.Current.Dispatcher.Invoke(() => ShowAlert(alerts));
        TrayDisplaySettings.Changed += OnTrayDisplayChanged;
        TrayIconPromotion.Schedule(_icon, () => !_disposed);
        if (!TrayDisplaySettings.ShowInTray)
            _window.ShowNearTray();
    }

    private void OnTrayDisplayChanged(object? sender, EventArgs e)
    {
        _trayDisplayItem.Checked = TrayDisplaySettings.ShowInTray;
        if (TrayDisplaySettings.ShowInTray)
        {
            _icon.Visible = true;
            if (TrayIconPromotion.TryPromote())
            {
                _icon.Visible = false;
                _icon.Visible = true;
            }
            return;
        }

        _icon.Visible = false;
        if (!_window.IsVisible)
            _window.ShowNearTray();
    }

    public void Start() => _poller.Start();

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        TrayDisplaySettings.Changed -= OnTrayDisplayChanged;
        _poller.Dispose();
        _icon.Visible = false;
        _icon.Dispose();
        _currentIcon?.Dispose();
        _window.CloseForExit();
    }

    private void ApplySnapshot()
    {
        var devices = _poller.Snapshot;
        _window.Render(devices);
        var mouse = MouseBattery.Select(devices);
        var tooltip = mouse?.Percent is int percent
            ? TrimTooltip($"{mouse.Name} {percent}%")
            : "鼠标电量";
        _icon.Text = tooltip;
        var next = TrayIconPainter.Create(mouse?.Percent);
        _icon.Icon = next;
        _currentIcon?.Dispose();
        _currentIcon = next;
    }

    private void ShowAlert(IReadOnlyList<LowBatteryAlert> alerts)
    {
        if (alerts.Count == 0)
            return;

        _icon.BalloonTipTitle = "电量偏低";
        _icon.BalloonTipText = alerts.Count == 1
            ? $"{alerts[0].DeviceName} 剩余 {alerts[0].Percent}%，已低于 {alerts[0].Threshold}%"
            : string.Join("，", alerts.Select(alert => $"{alert.DeviceName} {alert.Percent}%"));
        _icon.ShowBalloonTip(8000);
    }

    private void Exit()
    {
        Dispose();
        Application.Current.Shutdown();
    }

    private static string TrimTooltip(string text) => text.Length <= 63 ? text : text[..63];
}
