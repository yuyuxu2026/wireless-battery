using WirelessBattery.Models;

namespace WirelessBattery.Polling;

public sealed class LowBatteryTracker
{
    private readonly int _warnPercent;
    private readonly int _criticalPercent;
    private readonly Dictionary<string, Flags> _sent = new();

    public LowBatteryTracker(int warnPercent = 20, int criticalPercent = 10)
    {
        _warnPercent = warnPercent;
        _criticalPercent = criticalPercent;
    }

    public IReadOnlyList<LowBatteryAlert> Observe(IEnumerable<DeviceBattery> devices)
    {
        var alerts = new List<LowBatteryAlert>();
        foreach (var device in devices)
        {
            if (device.Kind != BatteryReadingKind.Level || device.IsStale || device.Percent is not int percent)
                continue;

            var state = Get(device.Id);
            if (percent > _warnPercent)
            {
                state.WarnSent = false;
                state.CriticalSent = false;
                continue;
            }

            if (percent > _criticalPercent)
                state.CriticalSent = false;

            if (percent <= _criticalPercent && !state.CriticalSent)
            {
                state.CriticalSent = true;
                state.WarnSent = true;
                alerts.Add(new LowBatteryAlert(device.Id, device.Name, percent, _criticalPercent));
            }
            else if (percent <= _warnPercent && !state.WarnSent)
            {
                state.WarnSent = true;
                alerts.Add(new LowBatteryAlert(device.Id, device.Name, percent, _warnPercent));
            }
        }

        return alerts;
    }

    private Flags Get(string id)
    {
        if (!_sent.TryGetValue(id, out var state))
        {
            state = new Flags();
            _sent[id] = state;
        }

        return state;
    }

    private sealed class Flags
    {
        public bool WarnSent { get; set; }

        public bool CriticalSent { get; set; }
    }
}
