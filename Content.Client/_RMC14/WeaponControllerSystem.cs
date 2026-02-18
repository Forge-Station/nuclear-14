using System.Diagnostics.CodeAnalysis;
using Content.Shared.WeaponMounts;
using Robust.Client.Player;


namespace Content.Client.WeaponMounts;


public sealed class WeaponControllerSystem : SharedWeaponControllerSystem
{
    [Dependency] private readonly IPlayerManager _player = default!;

    public bool TryGetLocalWeapon([NotNullWhen(true)] out EntityUid? weapon)
    {
        weapon = null;

        return _player.LocalEntity is { } user
            && TryGetControlledWeapon(user, out weapon, out _);
    }
}
