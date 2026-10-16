// Forge-Change: let melee NPCs keep chasing a remembered player beyond the usual 14-tile loss radius.
namespace Content.Server.NPC.Systems;

public sealed partial class NPCCombatSystem
{
    [Dependency] private readonly NPCRetaliationSystem _retaliation = default!;

    private bool IsRememberedMeleeAttacker(EntityUid uid, EntityUid target)
    {
        return _retaliation.IsRememberedPlayerAttacker(uid, target);
    }
}
