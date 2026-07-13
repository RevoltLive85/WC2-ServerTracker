using System.Text;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Menu;
using WC2.API.Interfaces;
using WC2.Shared.Extensions;
using WC2.Shared.Pooling;

namespace WC2.UI.Services;

/// <summary>
/// Compositor for player HUDs. Modules register widget providers; each HUD tick we
/// concatenate visible widgets (by slot, then priority) into one CenterHtml payload.
/// Single writer = no modules fighting over the center-html channel.
/// </summary>
public sealed class HudService : IHudService
{
    private sealed record Widget(string Id, HudSlot Slot, int Priority, Func<CCSPlayerController, string?> Provider);
    private sealed record ToastMessage(string Html, DateTime UntilUtc);

    private readonly List<Widget> _widgets = new();
    private readonly Dictionary<ulong, Queue<ToastMessage>> _toasts = new(64);
    private (string Html, DateTime UntilUtc)? _banner;
    private readonly Dictionary<ulong, (string Html, DateTime UntilUtc)> _welcome = new(64);
    private readonly Dictionary<ulong, string> _composed = new(64);
    private readonly ObjectPool<StringBuilder> _sbPool = new(() => new StringBuilder(512), sb => sb.Clear(), prewarm: 4);

    public void SetWidget(string widgetId, HudSlot slot, int priority, Func<CCSPlayerController, string?> provider)
    {
        RemoveWidget(widgetId);
        _widgets.Add(new Widget(widgetId, slot, priority, provider));
        _widgets.Sort(static (a, b) => a.Slot != b.Slot ? a.Slot.CompareTo(b.Slot) : b.Priority.CompareTo(a.Priority));
    }

    public void RemoveWidget(string widgetId) =>
        _widgets.RemoveAll(w => w.Id == widgetId);

    public void Toast(CCSPlayerController player, string html, float seconds = 4f)
    {
        if (!player.IsRealPlayer()) return;
        if (!_toasts.TryGetValue(player.SteamID, out var q)) _toasts[player.SteamID] = q = new Queue<ToastMessage>(4);
        q.Enqueue(new ToastMessage(html, DateTime.UtcNow.AddSeconds(seconds)));
    }

    public void ToastAll(string html, float seconds = 4f)
    {
        foreach (var p in PlayerExtensions.RealPlayers()) Toast(p, html, seconds);
    }

    public void Banner(string title, string subtitle, string colorHex, float seconds = 6f)
    {
        var html = $"<font class='fontSize-xl' color='{colorHex}'><b>{title}</b></font><br>" +
                   $"<font class='fontSize-m' color='#e8e2d0'><i>{subtitle}</i></font>";
        _banner = (html, DateTime.UtcNow.AddSeconds(seconds));
    }

    /// <summary>Per-player welcome panel: takes over that player's center-HTML for a few
    /// seconds on first spawn. Composed here (single writer) so it can't be stomped by the
    /// per-tick HUD repaint — the collision that made a standalone welcome plugin invisible.</summary>
    public void ShowWelcome(CCSPlayerController player, string html, float seconds)
    {
        if (!player.IsRealPlayer()) return;
        _welcome[player.SteamID] = (html, DateTime.UtcNow.AddSeconds(seconds));
    }

    /// <summary>Called every game tick by Plugin.cs. Re-sending the cached HTML each tick
    /// prevents the CenterHtml panel's fade-out, which otherwise renders as a blinking white box.</summary>
    public void Repaint()
    {
        foreach (var player in PlayerExtensions.RealPlayers())
        {
            // A CenterHtml menu (vendor, class picker...) owns the channel while open.
            if (MenuManager.GetActiveMenu(player) is not null) continue;
            if (_composed.TryGetValue(player.SteamID, out var html))
                player.PrintToCenterHtml(html);
        }
    }

    /// <summary>Called on a 0.5s timer by Plugin.cs. Composes widget HTML per player (cheap-ish);
    /// Repaint() then re-sends the cached result every tick.</summary>
    public void Render()
    {
        var now = DateTime.UtcNow;
        if (_banner is { } b && b.UntilUtc < now) _banner = null;

        foreach (var player in PlayerExtensions.RealPlayers())
        {
            var sb = _sbPool.Rent();
            try
            {
                // Per-player welcome takes precedence over everything for its short lifetime.
                if (_welcome.TryGetValue(player.SteamID, out var w))
                {
                    if (w.UntilUtc >= now) { _composed[player.SteamID] = w.Html; _sbPool.Return(sb); continue; }
                    _welcome.Remove(player.SteamID); // expired
                }

                // Banner overrides everything else — cinematic moments deserve the whole screen.
                if (_banner is { } banner) sb.Append(banner.Html);
                else
                {
                    if (_toasts.TryGetValue(player.SteamID, out var q))
                    {
                        while (q.Count > 0 && q.Peek().UntilUtc < now) q.Dequeue();
                        if (q.Count > 0) sb.Append(q.Peek().Html).Append("<br>");
                    }
                    for (var i = 0; i < _widgets.Count; i++)
                    {
                        var html = _widgets[i].Provider(player);
                        if (string.IsNullOrEmpty(html)) continue;
                        if (sb.Length > 0) sb.Append("<br>");
                        sb.Append(html);
                    }
                }
                if (sb.Length > 0) _composed[player.SteamID] = sb.ToString();
                else _composed.Remove(player.SteamID);
            }
            finally { _sbPool.Return(sb); }
        }
    }
}
