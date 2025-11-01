using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Content.Shared.Mind.Components;
using Content.Shared.Interaction.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.Inventory.Events;
using Robust.Shared.Prototypes;


namespace Content.Server._NC.ClothingFactionAndRoleAdd;

public sealed partial class ClothingFactionAndRoleAddSystem : EntitySystem
{
    [Dependency] private readonly SharedJobSystem _job = default!;
    [Dependency] private readonly NpcFactionSystem _npcFaction = default!;
    [Dependency] private readonly SharedRoleSystem _roles = default!;
    private Dictionary<EntityUid, List<string>> _removedFactions = new();
    private Dictionary<EntityUid, string> _removedJobs = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ClothingFactionAndRoleAddComponent, GotEquippedEvent>(OnClothingEquipped);
        SubscribeLocalEvent<ClothingFactionAndRoleAddComponent, GotUnequippedEvent>(OnClothingUnequipped);
    }

    private void OnClothingEquipped(EntityUid uid, ClothingFactionAndRoleAddComponent component, GotEquippedEvent args)
    {
        var user = args.Equipee;
        if (!TryComp(user, out MindContainerComponent? mind) || !mind.HasMind)
            return;

        var mindId = mind.Mind.Value;
        if (_job.MindTryGetJob(mindId, out JobPrototype? prototype)
            && !_job.MindHasJobWithId(mindId, component.JobId))
        {
            if (!_removedJobs.ContainsKey(user))
            {
                _removedJobs[user] = prototype.ID;
                _roles.MindTryRemoveRole<JobRoleComponent>(mindId);
            }
            _roles.MindAddRole(mindId, component.JobId);
        }
        else _roles.MindAddRole(mindId, component.JobId);

        if (TryComp(user, out NpcFactionMemberComponent? npc) && !_npcFaction.IsMember(user, component.Faction))
        {
            if (!_removedFactions.ContainsKey(user))
                _removedFactions[user] = new List<string>();

            foreach (var faction in npc.Factions)
            {
                _npcFaction.RemoveFaction(user, faction);
                _removedFactions[user].Add(faction);
            }
            _npcFaction.AddFaction(user, component.Faction);
        }

        AddComp<UnremoveableComponent>(uid);
        component.IsActive = true;
    }

    private void OnClothingUnequipped(EntityUid uid, ClothingFactionAndRoleAddComponent component, GotUnequippedEvent args)
    {
        if (!component.IsActive)
            return;

        var user = args.Equipee;

        if (!TryComp(user, out MindContainerComponent? mind) || !mind.HasMind)
            return;

        var mindId = mind.Mind.Value;
        if (_roles.MindTryRemoveRole<JobRoleComponent>(mindId))
        {
            if (_removedJobs.TryGetValue(user, out var job))
            {
                _roles.MindAddRole(mindId, job);
                _removedJobs.Remove(user);
            }
        }

        if (_removedFactions.TryGetValue(user, out var factions))
        {
            _npcFaction.RemoveFaction(user, component.Faction);
            foreach (var faction in factions)
            {
                _npcFaction.AddFaction(user, faction);
            }

            _removedFactions.Remove(user);
        }
        component.IsActive = false;
    }
}
