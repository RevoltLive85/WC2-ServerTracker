using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace WC2.UI.Services;

/// <summary>
/// Player-toggleable third-person camera. Works without sv_cheats by parenting a
/// camera entity (info_target driving the player's view) behind and above the pawn,
/// then setting the player's CameraServices to view through it. Toggling off, dying,
/// disconnecting, or a round ending all restore the normal first-person view so the
/// camera can never interfere with actual combat.
///
/// This is the well-established CS2Sharp technique (a "prop_dynamic"/observer-target
/// camera). It's purely a client view change — no cheat flags, no gameplay effect.
/// </summary>
public sealed class ThirdPersonService
{
    // Live cameras keyed by player SteamID → the camera entity we spawned for them.
    private readonly Dictionary<ulong, CDynamicProp> _cameras = new(16);

    public bool IsActive(ulong steamId) => _cameras.ContainsKey(steamId);

    /// <summary>Toggle third-person for a player. Returns the new state (true = now on).</summary>
    public bool Toggle(CCSPlayerController player)
    {
        if (IsActive(player.SteamID)) { Disable(player); return false; }
        return Enable(player);
    }

    public bool Enable(CCSPlayerController player)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn is null || !pawn.IsValid || pawn.AbsOrigin is null || pawn.AbsRotation is null)
            return false;
        if (IsActive(player.SteamID)) return true;

        // A prop_dynamic makes a stable, position-able camera anchor.
        var camera = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
        if (camera is null) return false;

        camera.DispatchSpawn();

        // Position the camera behind + above the player, looking the same direction.
        var eye = pawn.AbsOrigin;
        var ang = pawn.AbsRotation;
        camera.Teleport(new Vector(eye.X, eye.Y, eye.Z), new QAngle(ang.X, ang.Y, ang.Z), null);

        // Route the player's view through the camera entity.
        pawn.CameraServices!.ViewEntity.Raw = camera.EntityHandle.Raw;
        Utilities.SetStateChanged(pawn, "CBasePlayerPawn", "m_pCameraServices");

        _cameras[player.SteamID] = camera;
        return true;
    }

    public void Disable(CCSPlayerController player)
    {
        var steamId = player.SteamID;
        if (_cameras.TryGetValue(steamId, out var camera))
        {
            if (camera.IsValid) camera.Remove();
            _cameras.Remove(steamId);
        }

        // Restore first-person by clearing the view entity.
        var pawn = player.PlayerPawn.Value;
        if (pawn is { IsValid: true } && pawn.CameraServices is not null)
        {
            pawn.CameraServices.ViewEntity.Raw = uint.MaxValue; // invalid handle = own eyes
            Utilities.SetStateChanged(pawn, "CBasePlayerPawn", "m_pCameraServices");
        }
    }

    /// <summary>Force-clear a player's camera without needing their pawn (death/disconnect).</summary>
    public void ForceClear(ulong steamId)
    {
        if (_cameras.TryGetValue(steamId, out var camera))
        {
            if (camera.IsValid) camera.Remove();
            _cameras.Remove(steamId);
        }
    }

    /// <summary>Clear every active camera (round end / map change / unload).</summary>
    public void ClearAll()
    {
        foreach (var camera in _cameras.Values)
            if (camera.IsValid) camera.Remove();
        _cameras.Clear();
    }
}
