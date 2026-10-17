using Robust.Shared.Serialization;

namespace Content.Shared.Body.Part;

/// <summary>
/// What kind of body part this is.
///
/// Fork-owned: upstream's Nubody rewrite dropped Content.Shared/Body/Part entirely and
/// keys organs by <see cref="OrganCategoryPrototype"/> instead. Shitmed still needs the
/// taxonomy for surgery, targeting and part sprites, so it lives here.
/// </summary>
[Serializable, NetSerializable]
public enum BodyPartType
{
    Other = 0,
    Torso,
    Head,
    Arm,
    Hand,
    Leg,
    Foot,
    Tail,
}
