using Content.Shared.Humanoid;

namespace Content.Shared.Body;

/// <summary>
/// Forge-Change: цвет глаз как одно значение у тела.
/// </summary>
/// <remarks>
/// До Nubody цвет глаз лежал полем на гуманоиде, и форковые системы (теневики, кровавый
/// культ) просто читали и писали его. Теперь он живёт в профиле органа-глаза
/// (<see cref="VisualOrganComponent"/>), и таких органов у тела может быть несколько.
/// Эти помощники прячут перебор: читают цвет первого найденного глаза и ставят его всем.
/// </remarks>
public abstract partial class SharedVisualBodySystem
{
    [Dependency] private readonly BodySystem _forgeBody = default!;

    /// <summary>
    /// Цвет глаз тела, если у него вообще есть орган-глаз.
    /// </summary>
    public bool TryGetEyeColor(EntityUid body, out Color color)
    {
        color = default;
        foreach (var organ in _forgeBody.EnumerateOrgans<VisualOrganComponent>(body))
        {
            if (!organ.Comp2.Layer.Equals(HumanoidVisualLayers.Eyes))
                continue;

            color = organ.Comp2.Profile.EyeColor;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Ставит цвет всем органам-глазам тела.
    /// </summary>
    public void SetEyeColor(EntityUid body, Color color)
    {
        foreach (var organ in _forgeBody.EnumerateOrgans<VisualOrganComponent>(body))
        {
            if (!organ.Comp2.Layer.Equals(HumanoidVisualLayers.Eyes))
                continue;

            var profile = organ.Comp2.Profile;
            profile.EyeColor = color;
            organ.Comp2.Profile = profile;
            Dirty(organ.Owner, organ.Comp2);
            SetOrganColor((organ.Owner, organ.Comp2), color);
        }
    }
}
