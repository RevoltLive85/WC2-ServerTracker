using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace WC2.Shared.Extensions;

public static class PlayerExtensions
{
    public static bool IsRealPlayer(this CCSPlayerController? p) =>
        p is { IsValid: true, IsBot: false, IsHLTV: false } && p.SteamID != 0;

    public static IEnumerable<CCSPlayerController> RealPlayers()
    {
        // Utilities.GetPlayers allocates a list once; we filter inline, no LINQ chains in hot paths.
        foreach (var p in Utilities.GetPlayers())
            if (p.IsRealPlayer()) yield return p;
    }

    public static int RealPlayerCount()
    {
        var n = 0;
        foreach (var p in Utilities.GetPlayers()) if (p.IsRealPlayer()) n++;
        return n;
    }
}
