// Types upstream removed during its refactors that fork content still needs.
// Kept in one fork-owned file so later merges cannot take them again.
using System.Numerics;
using Robust.Shared.Serialization;

namespace Content.Shared.Chemistry.Components
{
    /// <summary>
    /// Possible modes for an <see cref="InjectorComponent"/>.
    /// </summary>
    public enum InjectorToggleMode : byte
    {
        /// <summary>
        /// The injector will try to inject reagent into things.
        /// </summary>
        Inject,

        /// <summary>
        /// The injector will try to draw reagent from things.
        /// </summary>
        Draw,
    }
}

namespace Content.Shared.Nutrition.Components
{
    [Serializable, NetSerializable]
    public enum ButcheringType : byte
    {
        Knife, // e.g. goliaths
        Spike, // e.g. monkeys
        Gibber, // e.g. humans
    }
}

namespace Content.Shared.Cloning
{
    [Serializable, NetSerializable]
    public enum ForcedMetempsychosisType : byte
    {
        None,
        Clone,
        RandomHumanoid,
        RandomNonHumanoid,
    }
}

namespace Content.Shared.Blocking
{
    /// <summary>
    /// Raised directed on the blocking object when attempting to block.
    /// </summary>
    public sealed class BeforeBlockingEvent(EntityUid user, EntityUid? origin) : CancellableEntityEventArgs
    {
        public EntityUid User = user;
        public EntityUid? Origin = origin;
    }
}

namespace Content.Shared.Storage.Components
{
    [ByRefEvent]
    public record struct StoreMobInItemContainerAttemptEvent(bool Handled, bool Cancelled = false);
}

namespace Content.Shared.Clothing.EntitySystems
{
    /// <summary>
    /// Raised on toggleable clothing when someone tries to toggle it.
    /// Removed upstream along with the modsuit rework; the fork's sealable clothing still raises it.
    /// </summary>
    public sealed class ToggleClothingAttemptEvent : CancellableEntityEventArgs
    {
        public EntityUid User { get; }
        public EntityUid Target { get; }

        public ToggleClothingAttemptEvent(EntityUid user, EntityUid target)
        {
            User = user;
            Target = target;
        }
    }
}
