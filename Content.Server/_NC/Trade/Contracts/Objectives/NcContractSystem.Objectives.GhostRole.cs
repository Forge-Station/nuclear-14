using Content.Server.Atmos.Rotting;
using Content.Server.Cuffs;
using Content.Server.Humanoid;
using Content.Shared.Movement.Systems;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem : EntitySystem
{
    [Dependency] private readonly CuffableSystem _contractGhostRoleCuffs = default!;
    [Dependency] private readonly HumanoidAppearanceSystem _contractGhostRoleHumanoid = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _contractGhostRoleMovement = default!;
    [Dependency] private readonly RottingSystem _contractGhostRoleRotting = default!;
}
