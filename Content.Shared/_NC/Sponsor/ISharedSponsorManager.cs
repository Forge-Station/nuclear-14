using System.Diagnostics.CodeAnalysis;
using Robust.Shared.Network;

namespace Content.Shared._NC.Sponsors;

public interface ISharedSponsorManager
{
    void Initialize();
    bool TryGetSponsor(NetUserId user, [NotNullWhen(true)] out SponsorLevel level);
}
