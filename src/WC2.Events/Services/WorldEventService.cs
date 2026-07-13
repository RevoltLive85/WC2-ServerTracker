using Microsoft.Extensions.Logging;
using WC2.API.Events;
using WC2.API.Interfaces;
using WC2.Events.WorldEvents;

namespace WC2.Events.Services;

public sealed class WorldEventService : IWorldEventService
{
    private readonly IWc2EventBus _bus;
    private readonly ILogger _logger;
    private EventsFileConfig _config;
    private readonly Dictionary<string, IWorldEventHandler> _handlers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["multiplier"]      = new MultiplierEvent(),
        ["treasure_goblin"] = new TreasureGoblinEvent(),
        ["invasion"]        = new InvasionEvent(),
    };

    private EventsFileConfig.WorldEventDefinition? _active;
    private DateTime _activeUntilUtc;

    public string? ActiveEventId => _active?.Id;
    public float CurrentXpMultiplier => _active?.XpMultiplier ?? 1f;
    public float CurrentGoldMultiplier => _active?.GoldMultiplier ?? 1f;

    public WorldEventService(EventsFileConfig config, IWc2EventBus bus, ILogger logger)
    { _config = config; _bus = bus; _logger = logger; }

    public void ApplyConfig(EventsFileConfig config) => _config = config;

    public IReadOnlyList<string> GetEventIds()
    {
        var ids = new List<string>(_config.Events.Count);
        foreach (var e in _config.Events) ids.Add(e.Id);
        return ids;
    }

    public bool StartEvent(string eventId, string startedBy)
    {
        if (_active is not null) return false;
        EventsFileConfig.WorldEventDefinition? def = null;
        foreach (var e in _config.Events)
            if (string.Equals(e.Id, eventId, StringComparison.OrdinalIgnoreCase)) { def = e; break; }
        if (def is null || !_handlers.TryGetValue(def.Type, out var handler))
        {
            _logger.LogWarning("[WC2] Unknown event id/type: {Id}", eventId);
            return false;
        }

        _active = def;
        _activeUntilUtc = DateTime.UtcNow.AddSeconds(def.DurationSeconds);
        handler.OnStart(def);
        _bus.Publish(new WorldEventStartedEvent(def.Id, def.DisplayName));
        _logger.LogInformation("[WC2] World event {Id} started by {By}", def.Id, startedBy);
        return true;
    }

    public bool StopActiveEvent(string reason)
    {
        if (_active is null) return false;
        var def = _active;
        _active = null;
        if (_handlers.TryGetValue(def.Type, out var handler)) handler.OnEnd(def, reason);
        _bus.Publish(new WorldEventEndedEvent(def.Id, reason));
        return true;
    }

    /// <summary>1s tick: expiry + handler tick. Called from Plugin.cs timer.</summary>
    public void Tick()
    {
        if (_active is null) return;
        if (DateTime.UtcNow >= _activeUntilUtc) { StopActiveEvent("expired"); return; }
        if (_handlers.TryGetValue(_active.Type, out var handler)) handler.OnTick(_active);
    }

    public void MaybeStartRandomEvent()
    {
        if (_active is not null || Random.Shared.NextSingle() > _config.RandomEventChancePerRound) return;
        var candidates = new List<EventsFileConfig.WorldEventDefinition>();
        foreach (var e in _config.Events) if (e.Random) candidates.Add(e);
        if (candidates.Count == 0) return;
        StartEvent(candidates[Random.Shared.Next(candidates.Count)].Id, "random_rotation");
    }
}
