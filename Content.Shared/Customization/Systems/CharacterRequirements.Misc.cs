using Content.Shared.Preferences;
using Content.Shared.Roles;
using JetBrains.Annotations;
using Robust.Shared.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Content.Shared._NC.CorvaxVars; // Forge-Change
using Content.Shared._NC.Sponsors;
using Robust.Shared.Network;
using Content.Shared.Players; // Forge-Change

namespace Content.Shared.Customization.Systems;

/// <summary>
///     Requires the server to have a specific CVar value.
/// </summary>
[UsedImplicitly, Serializable, NetSerializable,]
public sealed partial class CVarRequirement : CharacterRequirement
{
    [DataField("cvar", required: true)]
    public string CVar;

    [DataField(required: true)]
    public string RequiredValue;

    public override bool IsValid(
        JobPrototype job,
        HumanoidCharacterProfile profile,
        Dictionary<string, TimeSpan> playTimes,
        bool whitelisted,
        IPrototype prototype,
        IEntityManager entityManager,
        IPrototypeManager prototypeManager,
        IConfigurationManager configManager,
        ISharedSponsorManager sponsorManager, // Forge-Change
        out string? reason,
        int depth = 0
    )
    {
        if (!configManager.IsCVarRegistered(CVar))
        {
            reason = null;
            return true;
        }

        const string color = "lightblue";
        var cvar = configManager.GetCVar(CVar);
        var isValid = cvar.ToString()! == RequiredValue;

        reason = Loc.GetString(
            "character-cvar-requirement",
            ("inverted", Inverted),
            ("color", color),
            ("cvar", CVar),
            ("value", RequiredValue));

        return isValid;
    }
}

[UsedImplicitly, Serializable, NetSerializable]
public sealed partial class MinPlayersRequirement : CharacterRequirement
{
    [DataField(required: true)]
    public int Min;

    public override bool IsValid(
        JobPrototype job,
        HumanoidCharacterProfile profile,
        Dictionary<string, TimeSpan> playTimes,
        bool whitelisted,
        IPrototype prototype,
        IEntityManager entityManager,
        IPrototypeManager prototypeManager,
        IConfigurationManager configManager,
        ISharedSponsorManager sponsorManager,
        out string? reason,
        int depth = 0)
    {
        var playerManager = IoCManager.Resolve<ISharedPlayerManager>();
        var playerCount = playerManager.PlayerCount;
        var cvar = configManager.GetCVar(CorvaxVars.MinPlayersRequirement);

        reason = Loc.GetString(
            "character-minPlayers-requirement",
            ("min", Min));

        if (!cvar)
            return true;

        return playerCount >= Min;
    }
}

