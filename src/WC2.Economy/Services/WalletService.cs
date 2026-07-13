using System.Text.Json;
using Microsoft.Extensions.Logging;
using WC2.API.Events;
using WC2.API.Interfaces;
using WC2.API.Models;

namespace WC2.Economy.Services;

/// <summary>
/// Player wallets with JSON persistence and write-behind saving.
/// Balances live in memory (game thread only); the dirty set is flushed to disk
/// on a timer + on unload, so the hot path never touches IO.
/// Swap this for a MySQL-backed implementation later without touching callers —
/// they only see IEconomyService.
/// </summary>
public sealed class WalletService
{
    private readonly string _dataDir;
    private readonly ILogger _logger;
    private readonly IWc2EventBus _bus;
    private readonly Dictionary<ulong, long[]> _wallets = new(128);
    private readonly HashSet<ulong> _dirty = new();

    private static readonly int CurrencyCount = Enum.GetValues<CurrencyType>().Length;

    public WalletService(string moduleDirectory, IWc2EventBus bus, ILogger logger)
    {
        _dataDir = Path.GetFullPath(Path.Combine(moduleDirectory, "..", "..", "wc2-data", "wallets"));
        Directory.CreateDirectory(_dataDir);
        _bus = bus; _logger = logger;
    }

    private long[] GetOrLoad(ulong steamId)
    {
        if (_wallets.TryGetValue(steamId, out var w)) return w;
        var path = Path.Combine(_dataDir, steamId + ".json");
        long[] wallet = new long[CurrencyCount];
        try
        {
            if (File.Exists(path))
            {
                var stored = JsonSerializer.Deserialize<Dictionary<string, long>>(File.ReadAllText(path));
                if (stored is not null)
                    foreach (var (k, v) in stored)
                        if (Enum.TryParse<CurrencyType>(k, out var c)) wallet[(int)c] = v;
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "[WC2] Failed loading wallet {SteamId}", steamId); }
        _wallets[steamId] = wallet;
        return wallet;
    }

    public long GetBalance(ulong steamId, CurrencyType currency) => GetOrLoad(steamId)[(int)currency];

    public void Grant(ulong steamId, CurrencyType currency, long amount, string source)
    {
        if (amount == 0) return;
        var w = GetOrLoad(steamId);
        var next = Math.Max(0, w[(int)currency] + amount);
        var delta = next - w[(int)currency];
        w[(int)currency] = next;
        _dirty.Add(steamId);
        _bus.Publish(new CurrencyChangedEvent(steamId, currency, delta, next, source));
    }

    public bool TrySpend(ulong steamId, CurrencyType currency, long amount, string sink)
    {
        var w = GetOrLoad(steamId);
        if (w[(int)currency] < amount) return false;
        w[(int)currency] -= amount;
        _dirty.Add(steamId);
        _bus.Publish(new CurrencyChangedEvent(steamId, currency, -amount, w[(int)currency], sink));
        return true;
    }

    /// <summary>Flush dirty wallets. Serialization happens on the game thread (cheap for a
    /// handful of small files); the actual disk write is fire-and-forget async.</summary>
    public void Flush()
    {
        if (_dirty.Count == 0) return;
        foreach (var steamId in _dirty)
        {
            if (!_wallets.TryGetValue(steamId, out var w)) continue;
            var dto = new Dictionary<string, long>(CurrencyCount);
            foreach (var c in Enum.GetValues<CurrencyType>()) dto[c.ToString()] = w[(int)c];
            var json = JsonSerializer.Serialize(dto);
            var path = Path.Combine(_dataDir, steamId + ".json");
            _ = File.WriteAllTextAsync(path, json);
        }
        _dirty.Clear();
    }
}
