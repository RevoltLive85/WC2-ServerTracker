using CounterStrikeSharp.API.Core;
using WC2.API.Interfaces;

namespace WC2.UI.Services;

/// <summary>Renders the WoW-style boss frame: name, title, segmented health bar, phase.</summary>
public sealed class BossBarWidget
{
    private const int Segments = 20;
    private readonly IBossService? _bosses;
    private readonly string _color;

    public BossBarWidget(IBossService? bosses, string color) { _bosses = bosses; _color = color; }

    public string? Render(CCSPlayerController _)
    {
        var boss = _bosses?.GetActiveBoss();
        if (boss is null) return null;

        var frac = boss.MaxHealth > 0 ? (float)boss.CurrentHealth / boss.MaxHealth : 0f;
        var filled = (int)MathF.Round(frac * Segments);

        // ▰▰▰▰▰▱▱ segmented bar reads instantly even in HTML hud constraints.
        Span<char> bar = stackalloc char[Segments];
        for (var i = 0; i < Segments; i++) bar[i] = i < filled ? '▰' : '▱';

        return $"<font color='{_color}'><b>{boss.DisplayName}</b></font> " +
               $"<font color='#c9c2b0'><i>{boss.Title}</i></font><br>" +
               $"<font color='{_color}'>{new string(bar)}</font> " +
               $"<font color='#ffffff'>{boss.CurrentHealth:N0} / {boss.MaxHealth:N0}</font> " +
               $"<font color='#9fdcff'>[{boss.PhaseName}]</font>";
    }
}
