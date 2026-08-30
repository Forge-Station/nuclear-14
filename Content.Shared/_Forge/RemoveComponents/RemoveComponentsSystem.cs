namespace Content.Shared._Forge.RemoveComponents;

public sealed class RemoveComponentsSystem : EntitySystem
{
    [Dependency] private readonly IComponentFactory _factory = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RemoveComponentsComponent, ComponentStartup>(OnStartup);
    }

    private void OnStartup(Entity<RemoveComponentsComponent> ent, ref ComponentStartup args)
    {
        foreach (var name in ent.Comp.Components)
        {
            if (_factory.TryGetRegistration(name, out var registration))
                EntityManager.RemoveComponentDeferred(ent.Owner, registration.Type);
        }
    }
}
