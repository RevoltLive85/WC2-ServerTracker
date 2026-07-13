using System.Reflection;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;
using WC2.API.Interfaces;

namespace WC2.Shared;

/// <summary>
/// Reflection bridge for NightFuryPrime/CS2-Warcraft-Plugin, verified against the
/// decompiled v4.1.1 surface:
///   WarcraftPlugin.WarcraftPlugin.Instance                       (public static property)
///   internal WarcraftPlayer GetWcPlayer(CCSPlayerController)     (internal instance method)
///   WarcraftPlayer.GetLevel()                                    (public method)
///   WarcraftPlayer 'className' private field / 'DesiredClass'    (race name)
///   WarcraftPlugin.XpSystem                                      (internal field; XP methods probed)
/// All lookups use NonPublic|Public flags because most of the plugin's API is internal.
/// If a future update renames members, calls degrade to defaults and log once.
/// </summary>
public sealed class WarcraftReflectionBridge : IWarcraftBridge
{
    private const BindingFlags Any =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    private readonly ILogger _logger;
    private object? _plugin;
    private MethodInfo? _getWcPlayer;      // (CCSPlayerController) -> WarcraftPlayer
    private object? _xpSystem;
    private MethodInfo? _xpMethod;
    private DateTime _nextBindAttemptUtc = DateTime.MinValue;
    private bool _warnedXp;

    public bool IsAvailable => EnsureBound();

    public WarcraftReflectionBridge(ILogger logger)
    {
        _logger = logger;
        EnsureBound();
    }

    private bool EnsureBound()
    {
        if (_plugin is not null) return true;
        if (DateTime.UtcNow < _nextBindAttemptUtc) return false;
        _nextBindAttemptUtc = DateTime.UtcNow.AddSeconds(10);

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = asm.GetType("WarcraftPlugin.WarcraftPlugin");
            if (type is null) continue;

            _plugin = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (_plugin is null) continue;   // plugin assembly present but not initialized yet

            _getWcPlayer = type.GetMethod("GetWcPlayer", Any, new[] { typeof(CCSPlayerController) });

            // XpSystem: internal field on the plugin; probe common grant-method names on it.
            _xpSystem = type.GetField("XpSystem", Any)?.GetValue(_plugin)
                     ?? type.GetProperty("XpSystem", Any)?.GetValue(_plugin);
            if (_xpSystem is not null)
            {
                foreach (var name in new[] { "AddXp", "GrantXp", "GiveXp", "AddExperience", "AddXpToPlayer" })
                {
                    _xpMethod = _xpSystem.GetType().GetMethod(name, Any);
                    if (_xpMethod is not null) break;
                }
            }

            _logger.LogInformation(
                "[WC2] Warcraft bridge bound: getWcPlayer={A} xpSystem={B} xpMethod={C}",
                _getWcPlayer is not null, _xpSystem is not null, _xpMethod?.Name ?? "none");
            return true;
        }
        _logger.LogInformation("[WC2] Warcraft plugin not visible yet; bridge will retry.");
        return false;
    }

    private static CCSPlayerController? FindController(ulong steamId)
    {
        foreach (var p in Utilities.GetPlayers())
            if (p is { IsValid: true } && p.SteamID == steamId) return p;
        return null;
    }

    private object? GetWcPlayer(ulong steamId)
    {
        if (!EnsureBound() || _getWcPlayer is null) return null;
        var controller = FindController(steamId);
        if (controller is null) return null;
        try { return _getWcPlayer.Invoke(_plugin, new object[] { controller }); }
        catch { return null; }
    }

    public int GetLevel(ulong steamId)
    {
        var wc = GetWcPlayer(steamId);
        if (wc is null) return 0;
        try
        {
            var m = wc.GetType().GetMethod("GetLevel", Any, Type.EmptyTypes);
            return m?.Invoke(wc, null) is int lvl ? lvl : 0;
        }
        catch { return 0; }
    }

    public string? GetRaceName(ulong steamId)
    {
        var wc = GetWcPlayer(steamId);
        if (wc is null) return null;
        var t = wc.GetType();
        try
        {
            return t.GetField("className", Any)?.GetValue(wc) as string
                ?? t.GetProperty("DesiredClass", Any)?.GetValue(wc) as string;
        }
        catch { return null; }
    }

    public bool GrantXp(ulong steamId, int amount, string reason)
    {
        if (!EnsureBound()) return false;
        if (_xpSystem is null || _xpMethod is null)
        {
            if (!_warnedXp)
            {
                _warnedXp = true;
                _logger.LogWarning("[WC2] XP grant unavailable: XpSystem method not found (probe list may need updating).");
            }
            return false;
        }

        var wc = GetWcPlayer(steamId);
        var controller = FindController(steamId);
        try
        {
            // Adapt to whatever signature the method has: fill params by type.
            var pars = _xpMethod.GetParameters();
            var args = new object?[pars.Length];
            for (var i = 0; i < pars.Length; i++)
            {
                var pt = pars[i].ParameterType;
                if (pt == typeof(CCSPlayerController)) args[i] = controller;
                else if (pt.Name == "WarcraftPlayer")  args[i] = wc;
                else if (pt == typeof(int))            args[i] = amount;
                else if (pt == typeof(float))          args[i] = (float)amount;
                else if (pt == typeof(string))         args[i] = reason;
                else args[i] = pars[i].HasDefaultValue ? pars[i].DefaultValue : null;
            }
            _xpMethod.Invoke(_xpSystem, args);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[WC2] GrantXp invocation failed");
            return false;
        }
    }

    public float GetUltimateCooldownRemaining(ulong steamId) => 0f; // not exposed by v4.1.1 surface
}
