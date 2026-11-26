using WirelessBattery.Models;
using WirelessBattery.Providers;

namespace WirelessBattery.Polling;

public sealed class BatteryPoller : IDisposable
{
    private readonly IBatteryProvider[] _providers;
    private readonly LowBatteryTracker _tracker;
    private readonly Dictionary<string, DateTimeOffset> _lastRun = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public BatteryPoller(IEnumerable<IBatteryProvider> providers, LowBatteryTracker tracker)
    {
        _providers = providers.ToArray();
        _tracker = tracker;
    }

    public event EventHandler? Updated;

    public event EventHandler<IReadOnlyList<LowBatteryAlert>>? Alerts;

    public IReadOnlyList<DeviceBattery> Snapshot { get; private set; } = [];

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => LoopAsync(_cts.Token));
    }

    public Task RefreshNowAsync() => PollAsync(force: true, _cts?.Token ?? CancellationToken.None);

    public void Dispose()
    {
        _cts?.Cancel();
        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        _cts?.Dispose();
        _gate.Dispose();
    }

    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            await PollAsync(force: true, cancellationToken);
            while (await timer.WaitForNextTickAsync(cancellationToken))
                await PollAsync(force: false, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task PollAsync(bool force, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var now = DateTimeOffset.Now;
            var due = _providers.Where(provider => force || IsDue(provider, now)).ToArray();
            if (due.Length == 0)
                return;

            var reads = await Task.WhenAll(due.Select(provider => ReadProviderAsync(provider, cancellationToken)));
            var byProvider = _providers.ToDictionary(provider => provider.Id, _ => (IReadOnlyList<DeviceBattery>)[]);
            for (var i = 0; i < due.Length; i++)
            {
                byProvider[due[i].Id] = reads[i];
                _lastRun[due[i].Id] = DateTimeOffset.Now;
            }

            if (!force)
            {
                foreach (var provider in _providers.Where(provider => !due.Contains(provider)))
                {
                    if (_previous.TryGetValue(provider.Id, out var previous))
                        byProvider[provider.Id] = previous;
                }
            }

            foreach (var pair in byProvider)
                _previous[pair.Key] = pair.Value;

            Snapshot = Dedupe(byProvider.Values.SelectMany(devices => devices));
            var alerts = _tracker.Observe(Snapshot);
            Updated?.Invoke(this, EventArgs.Empty);
            if (alerts.Count > 0)
                Alerts?.Invoke(this, alerts);
        }
        finally
        {
            _gate.Release();
        }
    }

    private readonly Dictionary<string, IReadOnlyList<DeviceBattery>> _previous = new();

    private bool IsDue(IBatteryProvider provider, DateTimeOffset now) =>
        !_lastRun.TryGetValue(provider.Id, out var last) || now - last >= provider.Interval;

    private static async Task<IReadOnlyList<DeviceBattery>> ReadProviderAsync(
        IBatteryProvider provider,
        CancellationToken cancellationToken)
    {
        try
        {
            return await provider.ReadAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return
            [
                new DeviceBattery
                {
                    Id = provider.Id + ":error",
                    Name = provider.Id,
                    Source = provider.Id,
                    Kind = BatteryReadingKind.Unavailable,
                    StatusText = "读取失败",
                    UpdatedAt = DateTimeOffset.Now
                }
            ];
        }
    }

    private static List<DeviceBattery> Dedupe(IEnumerable<DeviceBattery> devices) =>
        devices
            .GroupBy(device => device.Source == "笔记本" ? device.Id : device.Name.Trim(), StringComparer.CurrentCultureIgnoreCase)
            .Select(group => group.OrderBy(Score).First())
            .OrderBy(device => device.Source == "笔记本" ? 1 : device.Kind == BatteryReadingKind.Level ? 0 : device.Kind == BatteryReadingKind.Unavailable ? 2 : 3)
            .ThenBy(device => device.Percent ?? 101)
            .ThenBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private static int Score(DeviceBattery device) => device.Kind switch
    {
        BatteryReadingKind.Level => 0,
        BatteryReadingKind.Unavailable => 1,
        _ => 2
    };
}
