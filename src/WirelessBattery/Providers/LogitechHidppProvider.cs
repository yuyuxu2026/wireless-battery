using System.Text;
using HidSharp;
using WirelessBattery.Models;

namespace WirelessBattery.Providers;

public sealed class LogitechHidppProvider : IBatteryProvider
{
    private const int VendorId = 0x046D;
    private const int ProductId = 0xC54D;
    private const byte SoftwareId = 0x0A;
    private const int StaleAfterFailures = 3;
    private readonly Dictionary<int, CacheEntry> _cache = new();
    private readonly object _gate = new();

    public string Id => "logitech";

    public TimeSpan Interval => TimeSpan.FromSeconds(60);

    public Task<IReadOnlyList<DeviceBattery>> ReadAsync(CancellationToken cancellationToken) =>
        Task.Run(() => Read(cancellationToken), cancellationToken);

    private IReadOnlyList<DeviceBattery> Read(CancellationToken cancellationToken)
    {
        var shorts = new List<HidDevice>();
        var longs = new List<HidDevice>();
        foreach (var device in DeviceList.Local.GetHidDevices(VendorId, ProductId))
        {
            if (!IsVendorCollection(device))
                continue;
            if (device.GetMaxOutputReportLength() <= 7)
                shorts.Add(device);
            else
                longs.Add(device);
        }

        if (shorts.Count == 0)
            return [];

        HidStream? shortStream = null;
        HidStream? longStream = null;
        try
        {
            shortStream = OpenFirst(shorts);
            longStream = OpenFirst(longs);
            if (shortStream is null || longStream is null)
                return [BusyRow(shortStream is null)];

            shortStream.ReadTimeout = 200;
            shortStream.WriteTimeout = 1000;
            if (longStream is not null)
            {
                longStream.ReadTimeout = 200;
                longStream.WriteTimeout = 1000;
            }

            var link = new HidppLink(shortStream, longStream);
            var devices = new List<DeviceBattery>();
            for (var slot = 1; slot <= 2; slot++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsPaired(link, slot))
                    continue;
                var name = ReadName(link, slot) ?? "罗技设备";
                devices.Add(ReadBattery(link, slot, name));
            }

            return devices;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException)
        {
            return [BusyRow(ex is UnauthorizedAccessException)];
        }
        finally
        {
            longStream?.Dispose();
            shortStream?.Dispose();
        }
    }

    private DeviceBattery ReadBattery(HidppLink link, int slot, string name)
    {
        var feature = FindBatteryFeature(link, (byte)slot);
        if (feature is null)
            return RememberFailure(slot, name);

        var statusFunction = feature.Value.Id == 0x1004 ? (byte)1 : (byte)0;
        var report = link.Transact(Request((byte)slot, feature.Value.Index, statusFunction), 2500, retries: 1, wakeDevice: true);
        if (report is null || HidppLink.IsError(report, out _))
            return RememberFailure(slot, name);

        var percent = report.Length > 4 ? report[4] : (byte)0;
        var level = report.Length > 5 ? report[5] : (byte)0;
        var status = report.Length > 6 ? report[6] : (byte)0;
        if (percent == 0 && status == 3)
            percent = 100;
        else if (percent == 0)
            percent = Approximate(level);

        var charging = status is 1 or 2 or 4;
        var text = status switch
        {
            1 or 2 or 4 => "充电中",
            3 => "已充满",
            0 => "使用中",
            _ => "电量状态异常"
        };
        var now = DateTimeOffset.Now;
        lock (_gate)
        {
            _cache[slot] = new CacheEntry
            {
                Percent = percent,
                IsCharging = charging,
                StatusText = text,
                UpdatedAt = now,
                Failures = 0
            };
        }

        return new DeviceBattery
        {
            Id = IdFor(slot),
            Name = name,
            Source = "罗技接收器",
            Kind = BatteryReadingKind.Level,
            Percent = Math.Clamp((int)percent, 0, 100),
            IsCharging = charging,
            StatusText = text,
            UpdatedAt = now
        };
    }

    private DeviceBattery RememberFailure(int slot, string name)
    {
        lock (_gate)
        {
            if (!_cache.TryGetValue(slot, out var cached))
            {
                return new DeviceBattery
                {
                    Id = IdFor(slot),
                    Name = name,
                    Source = "罗技接收器",
                    Kind = BatteryReadingKind.Unavailable,
                    StatusText = "鼠标未响应",
                    UpdatedAt = DateTimeOffset.Now
                };
            }

            cached.Failures++;
            var stale = cached.Failures >= StaleAfterFailures;
            return new DeviceBattery
            {
                Id = IdFor(slot),
                Name = name,
                Source = "罗技接收器",
                Kind = BatteryReadingKind.Level,
                Percent = cached.Percent,
                IsCharging = cached.IsCharging,
                StatusText = stale ? "电量已过期" : "暂时读不到，显示上次电量",
                UpdatedAt = cached.UpdatedAt,
                IsStale = stale
            };
        }
    }

    private static (ushort Id, byte Index)? FindBatteryFeature(HidppLink link, byte slot)
    {
        foreach (var feature in new ushort[] { 0x1004, 0x1000, 0x1001 })
        {
            var request = new byte[]
            {
                0x10, slot, 0x00, SoftwareId, (byte)(feature >> 8), (byte)feature, 0x00
            };
            var report = link.Transact(request, 2500, retries: 1, wakeDevice: true);
            if (report is null || HidppLink.IsError(report, out _) || report.Length < 5 || report[4] == 0)
                continue;
            return (feature, report[4]);
        }

        return null;
    }

    private static bool IsPaired(HidppLink link, int slot)
    {
        var sub = (byte)(0x20 + slot - 1);
        var report = link.Transact(new byte[] { 0x10, 0xFF, 0x83, 0xB5, sub, 0x00, 0x00 }, 1200, retries: 1);
        return report is not null && !HidppLink.IsError(report, out _) && report.Length > 8 && report[0] == 0x11;
    }

    private static string? ReadName(HidppLink link, int slot)
    {
        var sub = (byte)(0x40 + slot - 1);
        var report = link.Transact(new byte[] { 0x10, 0xFF, 0x83, 0xB5, sub, 0x00, 0x00 }, 1200, retries: 1);
        if (report is null || HidppLink.IsError(report, out _) || report.Length < 7 || report[0] != 0x11)
            return null;

        var length = Math.Min(report[5], (byte)(report.Length - 6));
        if (length == 0)
            return null;
        var name = Encoding.ASCII.GetString(report, 6, length).Trim('\0', ' ');
        return name.Length == 0 ? null : name;
    }

    private static byte[] Request(byte deviceIndex, byte featureIndex, byte function) =>
        [0x10, deviceIndex, featureIndex, (byte)((function << 4) | SoftwareId), 0x00, 0x00, 0x00];

    private static byte Approximate(byte level) => level switch
    {
        8 => 100,
        4 => 50,
        2 => 20,
        1 => 5,
        _ => 0
    };

    private static string IdFor(int slot) => $"hidpp:046D:C54D:{slot}";

    private static DeviceBattery BusyRow(bool denied = true) => new()
    {
        Id = "hidpp:046D:C54D",
        Name = "罗技接收器",
        Source = "罗技接收器",
        Kind = BatteryReadingKind.Unavailable,
        StatusText = denied ? "接收器被占用" : "接收器读取失败",
        UpdatedAt = DateTimeOffset.Now
    };

    private static HidStream? OpenFirst(IEnumerable<HidDevice> devices)
    {
        foreach (var device in devices)
        {
            try
            {
                return device.Open();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return null;
    }

    private static bool IsVendorCollection(HidDevice device)
    {
        try
        {
            var raw = device.GetRawReportDescriptor();
            for (var i = 0; i < raw.Length - 2; i++)
            {
                if (raw[i] == 0x06 && raw[i + 2] == 0xFF)
                    return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
        }

        return false;
    }

    private sealed class CacheEntry
    {
        public int Percent { get; set; }

        public bool IsCharging { get; set; }

        public string StatusText { get; set; } = "";

        public DateTimeOffset UpdatedAt { get; set; }

        public int Failures { get; set; }
    }

    /// <summary>
    /// Requests go out on the 7-byte collection. HID++ 2.0 replies come back on the 20-byte collection.
    /// </summary>
    private sealed class HidppLink
    {
        private readonly HidStream _shortStream;
        private readonly HidStream? _longStream;

        public HidppLink(HidStream shortStream, HidStream? longStream)
        {
            _shortStream = shortStream;
            _longStream = longStream;
        }

        public byte[]? Transact(byte[] request, int timeoutMs, int retries, bool wakeDevice = false)
        {
            byte[]? lastError = null;
            for (var attempt = 0; attempt <= retries; attempt++)
            {
                if (attempt == 0)
                    Flush();

                _shortStream.Write(request, 0, request.Length);
                var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                var retry = false;
                while (DateTime.UtcNow < deadline)
                {
                    var report = ReadAny();
                    if (report is null || !Matches(request, report))
                        continue;
                    if (IsError(report, out var code) && (code is 0x07 or 0x08 || (wakeDevice && code == 0x09)))
                    {
                        lastError = report;
                        retry = true;
                        break;
                    }

                    return report;
                }

                if (attempt < retries && (retry || wakeDevice))
                {
                    if (retry)
                    {
                        var extra = DateTime.UtcNow.AddMilliseconds(2000);
                        while (DateTime.UtcNow < extra)
                        {
                            var late = ReadAny();
                            if (late is not null && Matches(request, late) && !IsError(late, out _))
                                return late;
                        }
                    }

                    Thread.Sleep(300);
                    continue;
                }

                if (!retry)
                    return lastError;
            }

            return lastError;
        }

        public static bool IsError(byte[] report, out byte code)
        {
            if (report.Length > 5 && report[2] is 0x8F or 0xFF)
            {
                code = report[5];
                return true;
            }

            code = 0;
            return false;
        }

        private static bool Matches(byte[] request, byte[] report)
        {
            if (report.Length < 4 || report[1] != request[1])
                return false;
            if (report[2] == request[2] && report[3] == request[3])
                return true;
            return report.Length > 4 &&
                   report[2] is 0x8F or 0xFF &&
                   report[3] == request[2] &&
                   report[4] == request[3];
        }

        private void Flush()
        {
            var shortTimeout = _shortStream.ReadTimeout;
            var longTimeout = _longStream?.ReadTimeout ?? 0;
            _shortStream.ReadTimeout = 20;
            if (_longStream is not null)
                _longStream.ReadTimeout = 20;
            var buffer = new byte[32];
            for (var i = 0; i < 6; i++)
            {
                var pending = TryRead(_shortStream, buffer);
                if (_longStream is not null)
                    pending |= TryRead(_longStream, buffer);
                if (!pending)
                    break;
            }

            _shortStream.ReadTimeout = shortTimeout;
            if (_longStream is not null)
                _longStream.ReadTimeout = longTimeout;
        }

        private byte[]? ReadAny()
        {
            var buffer = new byte[32];
            if (TryCopy(_shortStream, buffer, out var report))
                return report;
            if (_longStream is not null && TryCopy(_longStream, buffer, out report))
                return report;
            return null;
        }

        private static bool TryCopy(HidStream stream, byte[] buffer, out byte[] report)
        {
            try
            {
                var count = stream.Read(buffer, 0, buffer.Length);
                report = buffer[..count];
                return true;
            }
            catch (TimeoutException)
            {
                report = [];
                return false;
            }
        }

        private static bool TryRead(HidStream stream, byte[] buffer)
        {
            try
            {
                stream.Read(buffer, 0, buffer.Length);
                return true;
            }
            catch (TimeoutException)
            {
                return false;
            }
        }
    }
}
