// Forge-Change: configuration for NPC sleep, retaliation, and diagnostics.
using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    /// Log pathfinding work and queue sizes every ten seconds for server profiling.
    /// </summary>
    public static readonly CVarDef<bool> NPCPathfindingProfile = CVarDef.Create("npc.pathfinding_profile", false);

    /// <summary>
    /// Sleep map NPCs when no player is nearby.
    /// </summary>
    public static readonly CVarDef<bool> NPCProximityEnabled = CVarDef.Create("npc.proximity_enabled", true);

    /// <summary>
    /// Seconds between proximity checks for each map NPC.
    /// </summary>
    public static readonly CVarDef<float> NPCProximityCheckInterval = CVarDef.Create("npc.proximity_check_interval", 5f);

    /// <summary>
    /// Maximum distance in tiles for visible NPC allies to assist after a player attack.
    /// </summary>
    public static readonly CVarDef<float> NPCRetaliationAssistRange = CVarDef.Create("npc.retaliation_assist_range", 7f);

    /// <summary>
    /// Seconds proximity NPCs remember a player who attacked them or an ally.
    /// </summary>
    public static readonly CVarDef<float> NPCPlayerRetaliationDuration = CVarDef.Create("npc.player_retaliation_duration", 30f);

    /// <summary>
    /// Maximum extra route length along grid axes, in tiles, when a proximity NPC chases a player.
    /// A negative value disables the detour limit.
    /// </summary>
    public static readonly CVarDef<float> NPCMaxPlayerChaseDetour = CVarDef.Create("npc.max_player_chase_detour", 12f);
}
