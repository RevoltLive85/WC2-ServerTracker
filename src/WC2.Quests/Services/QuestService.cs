using System.Text.Json;
using Microsoft.Extensions.Logging;
using WC2.API.Events;
using WC2.API.Interfaces;
using WC2.API.Models;
using WC2.Quests.Models;

namespace WC2.Quests.Services;

public sealed class QuestService : IQuestService
{
    private readonly string _dataDir;
    private readonly IWc2EventBus _bus;
    private readonly ILogger _logger;
    private QuestsFileConfig _config;
    private readonly Dictionary<ulong, PlayerQuestState> _states = new(128);
    private readonly HashSet<ulong> _dirty = new();

    public QuestService(string moduleDirectory, QuestsFileConfig config, IWc2EventBus bus, ILogger logger)
    {
        _dataDir = Path.GetFullPath(Path.Combine(moduleDirectory, "..", "..", "wc2-data", "quests"));
        Directory.CreateDirectory(_dataDir);
        _config = config; _bus = bus; _logger = logger;
    }

    public void ApplyConfig(QuestsFileConfig config) => _config = config;
    public void ReloadDefinitions() { }

    // ── Rotation: deterministic daily selection, no scheduler needed ──
    private static string DailyKey()  => DateTime.UtcNow.ToString("yyyy-MM-dd");
    private static string WeeklyKey() => $"{System.Globalization.ISOWeek.GetYear(DateTime.UtcNow)}-W{System.Globalization.ISOWeek.GetWeekOfYear(DateTime.UtcNow)}";

    /// <summary>Deterministic, process-independent seed from steamId + today's date.
    /// FNV-1a over the string form guarantees the same player gets the same daily set all
    /// day, every restart — unlike HashCode.Combine which is randomized per process.</summary>
    private static int StableDailySeed(ulong steamId)
    {
        var s = steamId + ":" + DailyKey();
        uint hash = 2166136261u;
        foreach (var c in s) { hash ^= c; hash *= 16777619u; }
        return unchecked((int)hash);
    }

    private List<QuestDefinition> ResolveActiveDefinitions(ulong steamId)
    {
        var result = new List<QuestDefinition>(_config.DailyQuestCount + 2);
        var dailies = new List<QuestDefinition>();
        foreach (var q in _config.Quests)
        {
            if (q.Cadence == QuestCadence.Daily) dailies.Add(q);
            else result.Add(q); // weeklies & achievements always active
        }
        // Seeded shuffle: same player + same day = same daily set.
        // Seed MUST be stable across server restarts: HashCode.Combine is salted with a
        // per-process random seed (.NET security default), so it silently reshuffled the
        // daily set every restart. Build a deterministic seed from steamId + the date string
        // by hand instead.
        var rng = new Random(StableDailySeed(steamId));
        for (var i = dailies.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (dailies[i], dailies[j]) = (dailies[j], dailies[i]);
        }
        for (var i = 0; i < Math.Min(_config.DailyQuestCount, dailies.Count); i++)
            result.Add(dailies[i]);
        return result;
    }

    private PlayerQuestState GetState(ulong steamId)
    {
        if (!_states.TryGetValue(steamId, out var state))
        {
            var path = Path.Combine(_dataDir, steamId + ".json");
            try
            {
                state = File.Exists(path)
                    ? JsonSerializer.Deserialize<PlayerQuestState>(File.ReadAllText(path)) ?? new PlayerQuestState()
                    : new PlayerQuestState();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[WC2] Quest state load failed for {SteamId}", steamId);
                state = new PlayerQuestState();
            }
            _states[steamId] = state;
        }

        // Reset stale cadences. Marking dirty is essential: without it the reset lives only
        // in memory, and a restart reloads the OLD completed quests from disk — which is
        // exactly why dailies appeared to never reset.
        var changed = false;
        if (state.DailyDateKey != DailyKey())
        {
            state.DailyDateKey = DailyKey();
            PruneCadence(state, QuestCadence.Daily);
            changed = true;
        }
        if (state.WeeklyDateKey != WeeklyKey())
        {
            state.WeeklyDateKey = WeeklyKey();
            PruneCadence(state, QuestCadence.Weekly);
            changed = true;
        }
        if (changed) { _dirty.Add(steamId); Flush(); } // persist immediately, don't wait for the timer
        return state;
    }

    private void PruneCadence(PlayerQuestState state, QuestCadence cadence)
    {
        foreach (var q in _config.Quests)
            if (q.Cadence == cadence)
            {
                state.Progress.Remove(q.Id);
                state.Completed.Remove(q.Id);
            }
    }

    public IReadOnlyList<QuestProgressSnapshot> GetActiveQuests(ulong steamId)
    {
        var state = GetState(steamId);
        var defs = ResolveActiveDefinitions(steamId);
        var list = new List<QuestProgressSnapshot>(defs.Count);
        foreach (var d in defs)
        {
            state.Progress.TryGetValue(d.Id, out var progress);
            list.Add(new QuestProgressSnapshot(d, progress, state.Completed.Contains(d.Id)));
        }
        return list;
    }

    public void ReportObjective(ulong steamId, QuestObjectiveType type, string? target = null, int amount = 1)
    {
        var state = GetState(steamId);
        foreach (var def in ResolveActiveDefinitions(steamId))
        {
            if (def.Objective != type || state.Completed.Contains(def.Id)) continue;
            if (def.Target is not null && !string.Equals(def.Target, target, StringComparison.OrdinalIgnoreCase)) continue;

            state.Progress.TryGetValue(def.Id, out var progress);
            progress += amount;
            state.Progress[def.Id] = progress;
            _dirty.Add(steamId);

            if (progress >= def.Required)
            {
                state.Completed.Add(def.Id);
                _bus.Publish(new QuestCompletedEvent(steamId, def));
            }
        }
    }

    public void Flush()
    {
        foreach (var steamId in _dirty)
            if (_states.TryGetValue(steamId, out var state))
                _ = File.WriteAllTextAsync(Path.Combine(_dataDir, steamId + ".json"),
                        JsonSerializer.Serialize(state));
        _dirty.Clear();
    }
}
