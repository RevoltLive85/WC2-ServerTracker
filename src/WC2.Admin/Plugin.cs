using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Admin;
using Microsoft.Extensions.Logging;
using WC2.Admin.Menus;

namespace WC2.Admin;

[MinimumApiVersion(200)]
public sealed class WC2AdminPlugin : BasePlugin
{
    public override string ModuleName => "WC2.Admin";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "ServerTracker.live";
    public override string ModuleDescription => "In-game admin menu: bosses, events, maps, players, reloads.";

    public override void Load(bool hotReload)
    {
        var menu = new AdminMenu(this);

        // Both aliases open the same menu; permission is checked inside (and on every action).
        AddCommand("css_wc_admin", "Open the WC2 admin menu", (player, _) =>
        {
            if (player is null) return;
            if (!AdminManager.PlayerHasPermissions(player, "@css/root"))
            { player.PrintToChat(" [WC2] Admins only."); return; }
            menu.OpenMain(player);
        });
        AddCommand("css_admin", "Open the WC2 admin menu", (player, _) =>
        {
            if (player is null) return;
            if (!AdminManager.PlayerHasPermissions(player, "@css/root"))
            { player.PrintToChat(" [WC2] Admins only."); return; }
            menu.OpenMain(player);
        });

        Logger.LogInformation("[WC2] Admin module loaded.");
    }
}
