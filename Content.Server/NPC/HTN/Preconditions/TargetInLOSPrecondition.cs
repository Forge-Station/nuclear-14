using Content.Server.Interaction;
/// Forge-Change
using Content.Server.Weather;

namespace Content.Server.NPC.HTN.Preconditions;

public sealed partial class TargetInLOSPrecondition : HTNPrecondition
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    private InteractionSystem _interaction = default!;
    /// Forge-Change
    private WeatherSystem _weather = default!;

    [DataField("targetKey")]
    public string TargetKey = "Target";

    [DataField("rangeKey")]
    public string RangeKey = "RangeKey";

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _interaction = sysManager.GetEntitySystem<InteractionSystem>();
        /// Forge-Change
        _weather = sysManager.GetEntitySystem<WeatherSystem>();
    }

    public override bool IsMet(NPCBlackboard blackboard)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (!blackboard.TryGetValue<EntityUid>(TargetKey, out var target, _entManager))
            return false;

        var range = blackboard.GetValueOrDefault<float>(RangeKey, _entManager);

        /// Forge-Change-Del return _interaction.InRangeUnobstructed(owner, target, range);
        /// Forge-Change-Start
        return _weather.CanSeeThroughWeather(owner, target) &&
            _interaction.InRangeUnobstructed(owner, target, range);
        /// Forge-Change-End
    }
}
