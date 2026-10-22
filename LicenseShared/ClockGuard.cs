using System.Text;
using System.Text.Json;

namespace LicenseShared;

public sealed record ClockCheckResult(bool RollbackDetected, DateTimeOffset LastSeenUtc);

/// <summary>
/// Best-effort protection against moving the system clock backwards.
/// The last observed instant is stored DPAPI-protected and bound to product + machine.
/// A fully offline application cannot defend against deleting this state file; server-side
/// validation is the only complete answer.
/// </summary>
public sealed class ClockGuard
{
    /// <summary>Small tolerance for NTP corrections and daylight/timezone quirks.</summary>
    public static TimeSpan RollbackTolerance { get; set; } = TimeSpan.FromMinutes(10);

    private static ReadOnlySpan<byte> Magic => "WLCCLK1"u8;

    private readonly string _productId;
    private readonly string _path;

    public ClockGuard(string productId, string? root = null)
    {
        _productId = productId;
        string baseRoot = root ?? LicensePaths.State;
        _path = Path.Combine(baseRoot, LicenseStore.SafeName(productId) + ".clock");
    }

    public string FilePath => _path;

    public ClockCheckResult Check(DateTimeOffset nowUtc, string fingerprint)
    {
        var lastSeen = Read(fingerprint);

        if (lastSeen is null)
        {
            Write(nowUtc, fingerprint);
            return new ClockCheckResult(false, nowUtc);
        }

        if (nowUtc < lastSeen.Value - RollbackTolerance)
            return new ClockCheckResult(true, lastSeen.Value);

        if (nowUtc > lastSeen.Value)
        {
            Write(nowUtc, fingerprint);
            return new ClockCheckResult(false, nowUtc);
        }

        return new ClockCheckResult(false, lastSeen.Value);
    }

    private DateTimeOffset? Read(string fingerprint)
    {
        if (!File.Exists(_path)) return null;

        try
        {
            byte[]? unprotected = Dpapi.TryUnprotect(File.ReadAllBytes(_path), Entropy(fingerprint));
            if (unprotected is null || unprotected.Length <= Magic.Length) return null;
            if (!unprotected.AsSpan(0, Magic.Length).SequenceEqual(Magic)) return null;

            string json = Encoding.UTF8.GetString(unprotected, Magic.Length, unprotected.Length - Magic.Length);
            var state = JsonSerializer.Deserialize<ClockState>(json);
            if (state is null || state.LastSeenUtc == default) return null;

            return new DateTimeOffset(state.LastSeenUtc, TimeSpan.Zero);
        }
        catch (Exception ex) when (ex is IOException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    private void Write(DateTimeOffset value, string fingerprint)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(new ClockState { LastSeenUtc = value.UtcDateTime });
        byte[] blob = new byte[Magic.Length + json.Length];
        Magic.CopyTo(blob);
        json.CopyTo(blob, Magic.Length);

        byte[] protectedBlob = Dpapi.Protect(blob, Entropy(fingerprint));
        LicenseStore.WriteAtomic(_path, protectedBlob);
    }

    private byte[] Entropy(string fingerprint) => Dpapi.Entropy("WLC1-CLOCK", _productId, fingerprint);

    private sealed class ClockState
    {
        public DateTime LastSeenUtc { get; set; }
    }
}
