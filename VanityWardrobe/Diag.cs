using UnityEngine;

namespace VanityWardrobe;

// The optional debug log (Debug Log in the config). Off, it costs a check and writes nothing. It is for finding out why something does not
// work for another player, so it says what this game sends and what it receives from others.
internal static class Diag
{
    public static bool On => MainConfig.debugLog != null && MainConfig.debugLog.Value;

    public static void Log(string text)
    {
        if (On)
        {
            Debug.Log("[VanityWardrobe] " + text);
        }
    }

    // A character's name for the log, and whether it is the local player
    public static string Who(VisEquipment vis)
    {
        if (vis == null)
        {
            return "(nobody)";
        }
        if (vis == FittingRoom.PreviewVis)
        {
            return "the fitting-room model";
        }
        Player player = vis.GetComponent<Player>();
        if (player == null)
        {
            return vis.name;
        }
        return (player == Player.m_localPlayer ? "you" : player.GetPlayerName() + " (another player)");
    }
}
