using CounterStrikeSharp.API;
using WC2.API.Capabilities;
using WC2.API.Events;
using WC2.API.Models;
using WC2.Shared.Extensions;

namespace WC2.Events.WorldEvents;

/// <summary>Diablo homage: a fleeing "goblin" (fast, low-HP tagged bot/avatar) that
/// showers whoever kills it with loot. v1 abstracts the chase into a timed hunt:
/// the next N kills during the event each roll the goblin loot table.</summary>
public sealed class TreasureGoblinEvent : IWorldEventHandler
{
    public static volatile bool Active;
    public static string LootTable = "default_boss";

    public void OnStart(EventsFileConfig.WorldEventDefinition def)
    {
        Active = true;
        LootTable = def.Parameters.GetValueOrDefault("loot_table", "default_boss");
        Server.PrintToChatAll(" \x10[WORLD]\x01 A Treasure Goblin scurries onto the battlefield! Kills shake loot loose!");
    }

    public void OnEnd(EventsFileConfig.WorldEventDefinition def, string reason)
    {
        Active = false;
        Server.PrintToChatAll(" \x10[WORLD]\x01 The Treasure Goblin escapes through a portal...");
    }

    /// <summary>Wired from the module's EventPlayerDeath hook while active.</summary>
    public static void OnPlayerKill(ulong killerSteamId)
    {
        if (!Active) return;
        var eco = Wc2Capabilities.Economy.GetOrNull();
        if (eco is null) return;
        foreach (var drop in eco.RollLoot(LootTable, 0.6f))
            if (drop.Currency is { } c)
                eco.Grant(killerSteamId, c, drop.Amount, "treasure_goblin");
    }
}
