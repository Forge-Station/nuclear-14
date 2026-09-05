using Content.Server.Interaction;
using Content.Server.Weather;

namespace Content.Server.NPC.HTN.Preconditions;

public sealed partial class TargetInLOSPrecondition : HTNPrecondition
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    private InteractionSystem _interaction = default!;
    private WeatherSystem _weather = default!;

    [DataField("targetKey")]
    public string TargetKey = "Target";

    [DataField("rangeKey")]
    public string RangeKey = "RangeKey";

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _interaction = sysManager.GetEntitySystem<InteractionSystem>();
        _weather = sysManager.GetEntitySystem<WeatherSystem>();
    }

    public override bool IsMet(NPCBlackboard blackboard)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (!blackboard.TryGetValue<EntityUid>(TargetKey, out var target, _entManager))
            return false;

        var range = blackboard.GetValueOrDefault<float>(RangeKey, _entManager);

        return _weather.CanSeeThroughWeather(owner, target) &&
            _interaction.InRangeUnobstructed(owner, target, range);
    }
}
