using Content.Server.Damage.Components;
using Content.Shared.CombatMode.Pacification;
using Content.Shared.Damage;
using Content.Shared.Throwing;
using Content.Shared.Damage.Systems;

namespace Content.Server.Damage.Systems
{
    /// <summary>
    /// Damages the thrown item when it lands.
    /// </summary>
    public sealed partial class DamageOnLandSystem : EntitySystem
    {
        [Dependency] private Shared.Damage.Systems.DamageableSystem _damageableSystem = default!;

        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<DamageOnLandComponent, LandEvent>(DamageOnLand);
            SubscribeLocalEvent<DamageOnLandComponent, AttemptPacifiedThrowEvent>(OnAttemptPacifiedThrow);
        }

        /// <summary>
        /// Prevent Pacified entities from throwing damaging items.
        /// </summary>
        private void OnAttemptPacifiedThrow(Entity<DamageOnLandComponent> ent, ref AttemptPacifiedThrowEvent args)
        {
            // Allow healing projectiles, forbid any that do damage:
            if (ent.Comp.Damage.AnyPositive())
            {
                args.Cancel("pacified-cannot-throw");
            }
        }

        private void DamageOnLand(EntityUid uid, DamageOnLandComponent component, ref LandEvent args)
        {
            _damageableSystem.TryChangeDamage(uid, component.Damage, component.IgnoreResistances);
        }
    }
}
