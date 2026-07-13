namespace WC2.API.Interfaces;

public interface IWorldEventService
{
    string? ActiveEventId { get; }
    bool StartEvent(string eventId, string startedBy);
    bool StopActiveEvent(string reason);
    /// <summary>Global XP multiplier composed from all active modifiers (Double XP weekend, invasions...).</summary>
    float CurrentXpMultiplier { get; }
    float CurrentGoldMultiplier { get; }
    /// <summary>All configured event ids (for admin UI).</summary>
    IReadOnlyList<string> GetEventIds();
}
