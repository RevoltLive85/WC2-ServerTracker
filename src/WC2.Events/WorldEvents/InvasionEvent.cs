using CounterStrikeSharp.API;
using WC2.API.Capabilities;

namespace WC2.Events.WorldEvents;

/// <summary>Random invasion: elevated rewards + a surprise boss from the configured pool.</summary>
public sealed class InvasionEvent : IWorldEventHandler
{
    public void OnStart(EventsFileConfig.WorldEventDefinition def)
    {
        Server.PrintToChatAll(" \x02[WORLD]\x01 The realm is under INVASION! Rewards increased — survive!");
        var pool = def.Parameters.GetValueOrDefault("boss_pool", "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (pool.Length == 0) return;
        var bossId = pool[Random.Shared.Next(pool.Length)];
        Wc2Capabilities.Bosses.GetOrNull()?.SpawnBoss(bossId, "invasion");
    }

    public void OnEnd(EventsFileConfig.WorldEventDefinition def, string reason)
    {
        Wc2Capabilities.Bosses.GetOrNull()?.DespawnActiveBoss("invasion_end");
        Server.PrintToChatAll(" \x06[WORLD]\x01 The invasion has been repelled!");
    }
}
