using Content.Server.Chat.Systems;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Server.RoundEnd;
using Content.Shared._Forge.Warfront;
using Content.Shared._Forge.Warfront.Components;
using Content.Shared.GameTicking.Components;
using Robust.Shared.Maths;

namespace Content.Server._Forge.Warfront.GameRule;

public sealed class WarfrontRuleSystem : GameRuleSystem<WarfrontRuleComponent>
{
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly RoundEndSystem _roundEnd = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        if (args.JobId == null)
            return;

        var query = QueryActiveRules();
        while (query.MoveNext(out _, out _, out var component, out _))
        {
            if (component.NcrJobs.Contains(args.JobId))
            {
                EnsureComp<WarfrontFactionComponent>(args.Mob).Faction = WarfrontFaction.NCR;
                return;
            }

            if (component.LegionJobs.Contains(args.JobId))
            {
                EnsureComp<WarfrontFactionComponent>(args.Mob).Faction = WarfrontFaction.Legion;
                return;
            }
        }
    }

    public void AnnounceCitadelCaptured(WarfrontFaction faction)
    {
        _chat.DispatchGlobalAnnouncement(
            Loc.GetString("warfront-citadel-captured-announcement", ("faction", GetFactionName(faction))),
            Loc.GetString("warfront-victory-sender"),
            colorOverride: Color.Gold);
    }

    public void DeclareVictory(WarfrontFaction faction)
    {
        var restartDelay = (TimeSpan?) null;
        var alreadyDecided = false;

        var query = EntityQueryEnumerator<WarfrontRuleComponent>();
        while (query.MoveNext(out _, out var rule))
        {
            if (rule.Winner != null)
            {
                alreadyDecided = true;
                break;
            }

            rule.Winner = faction;
            restartDelay = rule.RestartDelay;
        }

        if (alreadyDecided)
            return;

        _chat.DispatchGlobalAnnouncement(
            Loc.GetString("warfront-victory-announcement", ("faction", GetFactionName(faction))),
            Loc.GetString("warfront-victory-sender"),
            colorOverride: Color.Gold);

        _roundEnd.EndRound(restartDelay);
    }

    protected override void AppendRoundEndText(EntityUid uid,
        WarfrontRuleComponent component,
        GameRuleComponent gameRule,
        ref RoundEndTextAppendEvent args)
    {
        args.AddLine(component.Winner != null
            ? Loc.GetString("warfront-round-end-winner", ("faction", GetFactionName(component.Winner.Value)))
            : Loc.GetString("warfront-round-end-no-winner"));
    }

    private string GetFactionName(WarfrontFaction faction)
    {
        return Loc.GetString(faction == WarfrontFaction.NCR
            ? "capture-point-faction-ncr"
            : "capture-point-faction-legion");
    }
}
