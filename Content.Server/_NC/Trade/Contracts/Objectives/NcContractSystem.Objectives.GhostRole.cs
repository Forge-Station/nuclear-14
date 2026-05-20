using Content.Server.Atmos.Rotting;
using Content.Server.Cuffs;
using Content.Server.Ghost.Roles.Components;
using Content.Server.Humanoid;
using Content.Server.Mind.Commands;
using Content.Server.Roles;
using Content.Shared._NC.Trade;
using Content.Shared.Cuffs.Components;
using Content.Shared.Customization.Systems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem : EntitySystem
{
    [Dependency] private readonly RottingSystem _contractGhostRoleRotting = default!;
    [Dependency] private readonly CuffableSystem _contractGhostRoleCuffs = default!;
    [Dependency] private readonly HumanoidAppearanceSystem _contractGhostRoleHumanoid = default!;
}
