namespace WC2.Events.WorldEvents;

/// <summary>Pure-modifier event (Double XP, Gold Rush): all the work happens via
/// IWorldEventService.CurrentXpMultiplier consumed by other modules.</summary>
public sealed class MultiplierEvent : IWorldEventHandler
{
    public void OnStart(EventsFileConfig.WorldEventDefinition def) { }
    public void OnEnd(EventsFileConfig.WorldEventDefinition def, string reason) { }
}
