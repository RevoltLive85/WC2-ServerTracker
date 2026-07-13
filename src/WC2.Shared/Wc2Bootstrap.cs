using CounterStrikeSharp.API.Core.Capabilities;
using Microsoft.Extensions.Logging;
using WC2.API.Capabilities;
using WC2.API.Interfaces;

namespace WC2.Shared;

/// <summary>
/// Called by every module on Load. Whichever module loads first registers the
/// shared event bus AND the Warcraft reflection bridge; everyone else just
/// resolves them. Requires WC2.API.dll to live in counterstrikesharp/shared/
/// so all plugins agree on type identity.
/// </summary>
public static class Wc2Bootstrap
{
    public static IWc2EventBus EnsureCore(ILogger logger)
    {
        var bus = Wc2Capabilities.EventBus.GetOrNull();
        if (bus is null)
        {
            var created = new Wc2EventBus(logger);
            Capabilities.RegisterPluginCapability(Wc2Capabilities.EventBus, () => created);
            bus = created;
            logger.LogInformation("[WC2] Shared event bus registered by this module.");
        }

        if (Wc2Capabilities.Warcraft.GetOrNull() is null)
        {
            var bridge = new WarcraftReflectionBridge(logger);
            Capabilities.RegisterPluginCapability(Wc2Capabilities.Warcraft, () => bridge);
            logger.LogInformation("[WC2] Warcraft bridge registered by this module.");
        }
        return bus;
    }
}
