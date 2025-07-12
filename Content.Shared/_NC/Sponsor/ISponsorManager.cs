using System.Diagnostics.CodeAnalysis;
using Robust.Shared.Network;

namespace Content.Shared._NC.Sponsors;

public interface ISponsorsManager
{
    bool TryGetSponsorData(NetUserId userId, out SponsorData? sponsorData);
    bool TryGetSponsorColor(SponsorLevel level, out string? color);
    bool TryGetSponsorGhost(SponsorLevel level, out string? ghost);
}
