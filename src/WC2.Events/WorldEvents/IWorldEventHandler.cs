namespace WC2.Events.WorldEvents;

/// <summary>One handler per event *type*; instances are configured per event *id* from events.json.
/// New event types = new class + one registry line. No changes anywhere else.</summary>
public interface IWorldEventHandler
{
    void OnStart(EventsFileConfig.WorldEventDefinition def);
    void OnEnd(EventsFileConfig.WorldEventDefinition def, string reason);
    /// <summary>Optional 1s tick while active.</summary>
    void OnTick(EventsFileConfig.WorldEventDefinition def) { }
}
