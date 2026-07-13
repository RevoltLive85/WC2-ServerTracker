namespace WC2.Admin;

/// <summary>Read-only mirrors of other modules' config shapes. WC2.Admin never
/// references sibling modules — it reads their shared JSON files directly,
/// deserializing only the fields the menu needs. Unknown fields are ignored.</summary>
public sealed class EventsView
{
    public List<EventEntry> Events { get; set; } = new();
    public sealed class EventEntry
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
    }
}

public sealed class RegionsView
{
    public RotationView Rotation { get; set; } = new();
    public sealed class RotationView
    {
        public List<MapEntry> Maps { get; set; } = new();
    }
    public sealed class MapEntry
    {
        public string Map { get; set; } = "";
        public string? DisplayName { get; set; }
    }
}
