using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;
using WirelessBattery.Models;

namespace WirelessBattery.Providers;

public sealed class BleBatteryProvider : IBatteryProvider
{
    private const int StaleAfterFailures = 3;
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public string Id => "ble";

    public TimeSpan Interval => TimeSpan.FromMinutes(3);

    public async Task<IReadOnlyList<DeviceBattery>> ReadAsync(CancellationToken cancellationToken)
    {
        var rows = new List<DeviceBattery>();
        var names = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        try
        {
            var selector = BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected);
            var devices = await DeviceInformation.FindAllAsync(selector);
            foreach (var info in devices)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = await ReadLeDeviceAsync(info, cancellationToken);
                if (row is null || !names.Add(row.Name))
                    continue;
                rows.Add(row);
            }

            rows.AddRange(await ListClassicAsync(names, cancellationToken));
            return rows;
        }
        catch (UnauthorizedAccessException)
        {
            return [Problem("系统拒绝读取蓝牙电量")];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return rows.Count > 0 ? rows : [Problem("蓝牙电量读取失败")];
        }
    }

    private async Task<DeviceBattery?> ReadLeDeviceAsync(DeviceInformation info, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        BluetoothLEDevice? device = null;
        try
        {
            device = await BluetoothLEDevice.FromIdAsync(info.Id);
            if (device is null)
                return null;

            var address = device.BluetoothAddress.ToString("X12");
            var name = Clean(device.Name) ?? Clean(info.Name) ?? "蓝牙设备";
            if (IsNoise(name))
                return null;

            var services = await device.GetGattServicesForUuidAsync(GattServiceUuids.Battery, BluetoothCacheMode.Uncached)
                .AsTask(timeout.Token);
            if (services.Status == GattCommunicationStatus.AccessDenied)
                return RememberFailure(address, name, "系统拒绝读取蓝牙电量");
            if (services.Status != GattCommunicationStatus.Success || services.Services.Count == 0)
            {
                DisposeServices(services.Services);
                return Unsupported(name, "le:" + address);
            }

            var service = services.Services[0];
            try
            {
                var characteristics = await service.GetCharacteristicsForUuidAsync(
                    GattCharacteristicUuids.BatteryLevel,
                    BluetoothCacheMode.Uncached).AsTask(timeout.Token);
                if (characteristics.Status != GattCommunicationStatus.Success || characteristics.Characteristics.Count == 0)
                    return RememberFailure(address, name, "暂时读不到电量");

                var read = await characteristics.Characteristics[0]
                    .ReadValueAsync(BluetoothCacheMode.Uncached)
                    .AsTask(timeout.Token);
                if (read.Status != GattCommunicationStatus.Success || read.Value is null)
                    return RememberFailure(address, name, "暂时读不到电量");

                var reader = DataReader.FromBuffer(read.Value);
                var percent = Math.Clamp((int)reader.ReadByte(), 0, 100);
                var now = DateTimeOffset.Now;
                lock (_gate)
                {
                    _cache[address] = new CacheEntry
                    {
                        Name = name,
                        Percent = percent,
                        UpdatedAt = now,
                        Failures = 0
                    };
                }

                return Level(address, name, percent, now, stale: false, "使用中");
            }
            finally
            {
                service.Dispose();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var name = Clean(info.Name) ?? "蓝牙设备";
            return RememberFailure(info.Id, name, "暂时读不到电量");
        }
        finally
        {
            device?.Dispose();
        }
    }

    private static async Task<IReadOnlyList<DeviceBattery>> ListClassicAsync(
        HashSet<string> alreadyListed,
        CancellationToken cancellationToken)
    {
        var rows = new List<DeviceBattery>();
        var selector = BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected);
        var devices = await DeviceInformation.FindAllAsync(selector);
        foreach (var info in devices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Clean(info.Name);
            if (name is null || !alreadyListed.Add(name) || IsNoise(name))
                continue;

            rows.Add(Unsupported(name, "classic:" + info.Id));
        }

        return rows;
    }

    private DeviceBattery RememberFailure(string address, string name, string status)
    {
        lock (_gate)
        {
            if (!_cache.TryGetValue(address, out var cached))
            {
                return new DeviceBattery
                {
                    Id = "ble:" + address,
                    Name = name,
                    Source = "蓝牙",
                    Kind = BatteryReadingKind.Unavailable,
                    StatusText = status,
                    UpdatedAt = DateTimeOffset.Now
                };
            }

            cached.Failures++;
            var stale = cached.Failures >= StaleAfterFailures;
            return Level(
                address,
                cached.Name,
                cached.Percent,
                cached.UpdatedAt,
                stale,
                stale ? "电量已过期" : "暂时读不到，显示上次电量");
        }
    }

    private static DeviceBattery Level(
        string address,
        string name,
        int percent,
        DateTimeOffset updatedAt,
        bool stale,
        string status) => new()
    {
        Id = "ble:" + address,
        Name = name,
        Source = "蓝牙",
        Kind = BatteryReadingKind.Level,
        Percent = percent,
        IsCharging = false,
        StatusText = status,
        UpdatedAt = updatedAt,
        IsStale = stale
    };

    private static DeviceBattery Unsupported(string name, string id) => new()
    {
        Id = "bt:" + id,
        Name = name,
        Source = "蓝牙",
        Kind = BatteryReadingKind.Unsupported,
        StatusText = "已连接，系统未提供电量",
        UpdatedAt = DateTimeOffset.Now
    };

    private static DeviceBattery Problem(string status) => new()
    {
        Id = "ble:error",
        Name = "蓝牙设备",
        Source = "蓝牙",
        Kind = BatteryReadingKind.Unavailable,
        StatusText = status,
        UpdatedAt = DateTimeOffset.Now
    };

    private static void DisposeServices(IReadOnlyList<GattDeviceService> services)
    {
        foreach (var service in services)
            service.Dispose();
    }

    private static string? Clean(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        var trimmed = name.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static bool IsNoise(string name) =>
        name.Contains("Avrcp", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Transport", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Enumerator", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Service", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Generic ", StringComparison.OrdinalIgnoreCase);

    private sealed class CacheEntry
    {
        public string Name { get; set; } = "";

        public int Percent { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }

        public int Failures { get; set; }
    }
}
