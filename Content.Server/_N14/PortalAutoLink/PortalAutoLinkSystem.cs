using Content.Shared.Interaction;
using Content.Shared.Teleportation.Systems;

namespace Content.Server._N14.PortalAutoLink
{
    public sealed partial class PortalAutoLinkSystem : EntitySystem
    {
        [Dependency] private readonly LinkedEntitySystem _linkedEntitySystem = default!;
        [Dependency] private readonly IEntityManager _entityMgr = default!;

        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<PortalAutoLinkComponent, ComponentStartup>(HandleMapInitialization); // Forge-Change MapInit -> ComponentStartup
        }

        private void HandleMapInitialization(Entity<PortalAutoLinkComponent> entity, ref ComponentStartup eventArgs)
        {
            PerformAutoLink(entity, out _);
        }

        public bool PerformAutoLink(Entity<PortalAutoLinkComponent> entity, out EntityUid? linkedEntityId)
        {
            linkedEntityId = null;

            var entityEnumerator = EntityQueryEnumerator<PortalAutoLinkComponent>();
            while (entityEnumerator.MoveNext(out var currentEntityUid, out var currentAutoLinkComponent))
            {
                if (entity.Owner == currentEntityUid) // Forge-Change
                    continue;

                if (entity.Comp.LinkKey == currentAutoLinkComponent.LinkKey)
                {
                    if (_linkedEntitySystem.TryLink(entity, currentEntityUid, false))
                    {
                        RemComp<PortalAutoLinkComponent>(currentEntityUid);
                        RemComp<PortalAutoLinkComponent>(entity);
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
