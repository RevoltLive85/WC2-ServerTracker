using CounterStrikeSharp.API.Core.Capabilities;
using WC2.API.Interfaces;

namespace WC2.API.Capabilities;

/// <summary>
/// Cross-plugin service discovery. Each WC2 module registers its public
/// service here on load; consumers resolve it lazily so load order never matters.
/// This is THE only coupling point between modules — everything else is events.
/// </summary>
public static class Wc2Capabilities
{
    public static readonly PluginCapability<IWc2EventBus>    EventBus  = new("wc2:eventbus");
    public static readonly PluginCapability<IBossService>    Bosses    = new("wc2:bosses");
    public static readonly PluginCapability<IEconomyService> Economy   = new("wc2:economy");
    public static readonly PluginCapability<IQuestService>   Quests    = new("wc2:quests");
    public static readonly PluginCapability<IRegionService>  World     = new("wc2:world");
    public static readonly PluginCapability<IHudService>     Hud       = new("wc2:hud");
    public static readonly PluginCapability<IWorldEventService> Events = new("wc2:events");
    public static readonly PluginCapability<IWarcraftBridge> Warcraft  = new("wc2:warcraft-bridge");
}
