using CounterStrikeSharp.API.Core;

namespace WC2.API.Interfaces;

/// <summary>Single owner of player-facing screen real estate. Modules never call
/// PrintToCenterHtml directly — they submit widgets/toasts here so nothing overlaps.</summary>
public interface IHudService
{
    /// <summary>Registers/updates a named widget slot. Provider is polled each HUD tick; return null to hide.</summary>
    void SetWidget(string widgetId, HudSlot slot, int priority, Func<CCSPlayerController, string?> provider);
    void RemoveWidget(string widgetId);
    /// <summary>WoW-style toast (yellow center text with fade), queued per player.</summary>
    void Toast(CCSPlayerController player, string html, float seconds = 4f);
    void ToastAll(string html, float seconds = 4f);
    /// <summary>Cinematic full-width banner (region discovery, boss spawn).</summary>
    void Banner(string title, string subtitle, string colorHex, float seconds = 6f);
    void ShowWelcome(CCSPlayerController player, string html, float seconds);
}

public enum HudSlot { Top, Center, Bottom }
