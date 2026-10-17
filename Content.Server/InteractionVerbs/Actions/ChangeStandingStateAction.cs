using Content.Shared.InteractionVerbs;
using Content.Shared.Standing;

namespace Content.Server.InteractionVerbs.Actions;

[Serializable]
public sealed partial class ChangeStandingStateAction : InteractionAction
{
    [DataField]
    public bool MakeStanding, MakeLaying;

    public override bool CanPerform(InteractionArgs args, InteractionVerbPrototype proto, bool isBefore, VerbDependencies deps)
    {
        if (!deps.EntMan.HasComponent<StandingStateComponent>(args.Target))
            return false;

        // Апстрим свёл состояние к Standing/нет, промежуточного GettingUp больше нет.
        var down = deps.EntMan.System<StandingStateSystem>().IsDown(args.Target);

        if (isBefore)
            args.Blackboard["standing"] = !down;

        return down ? MakeStanding : MakeLaying;
    }

    public override bool Perform(InteractionArgs args, InteractionVerbPrototype proto, VerbDependencies deps)
    {
        var stateSystem = deps.EntMan.System<StandingStateSystem>();

        if (!deps.EntMan.HasComponent<StandingStateComponent>(args.Target))
            return false;

        var down = stateSystem.IsDown(args.Target);

        if (args.TryGetBlackboard("standing", out bool oldStanding) && oldStanding == down)
            return false;

        // Note: these will get cancelled if the target is forced to stand/lay, e.g. due to a buckle or stun or something else.
        if (down && MakeStanding)
            return stateSystem.Stand(args.Target);
        else if (!down && MakeLaying)
            return stateSystem.Down(args.Target);

        return false;
    }
}
