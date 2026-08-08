using System.Diagnostics.CodeAnalysis;
using Content.Shared._NC.Sponsor;
using JetBrains.Annotations;
using Robust.Shared.Network;

namespace Content.Server._NC.Sponsor;

[UsedImplicitly]
public sealed class SponsorManager : ISharedSponsorManager
{
    public void Initialize() { }
    public Dictionary<NetUserId, SponsorLevel> Sponsors = new();
    public bool TryGetSponsor(NetUserId user, [NotNullWhen(true)] out SponsorLevel level)
    {
        return Sponsors.TryGetValue(user, out level);
    }

    public bool TryGetSponsorColor(SponsorLevel level, [NotNullWhen(true)] out string? color)
    {
        return SponsorData.SponsorColor.TryGetValue(level, out color);
    }

    // A level can unlock several skins; this returns the first one as the level's default ghost.
    public bool TryGetSponsorGhost(SponsorLevel level, [NotNullWhen(true)] out string? ghost)
    {
        ghost = null;
        if (SponsorData.SponsorGhost.TryGetValue(level, out var ghosts) && ghosts.Count > 0)
        {
            ghost = ghosts[0];
            return true;
        }

        return false;
    }
}
