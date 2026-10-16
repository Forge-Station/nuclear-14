using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;

namespace Content.Shared.EntityEffects;

/// <summary>
/// Fork-owned entity effect arguments. They used to live in EntityEffect.cs, which
/// upstream rewrote, but the fork's EntityEffectCondition framework and its nine
/// conditions still take them, so they are kept in a separate file.
/// </summary>
public record class EntityEffectBaseArgs
{
    public EntityUid TargetEntity;

    public IEntityManager EntityManager = default!;

    public EntityEffectBaseArgs(EntityUid targetEntity, IEntityManager entityManager)
    {
        TargetEntity = targetEntity;
        EntityManager = entityManager;
    }
}

public record class EntityEffectReagentArgs : EntityEffectBaseArgs
{
    public EntityUid? OrganEntity;

    public Solution? Source;

    public FixedPoint2 Quantity;

    public ReagentPrototype? Reagent;

    public ReactionMethod? Method;

    public FixedPoint2 Scale;

    public EntityEffectReagentArgs(
        EntityUid targetEntity,
        IEntityManager entityManager,
        EntityUid? organEntity,
        Solution? source,
        FixedPoint2 quantity,
        ReagentPrototype? reagent,
        ReactionMethod? method,
        FixedPoint2 scale)
        : base(targetEntity, entityManager)
    {
        OrganEntity = organEntity;
        Source = source;
        Quantity = quantity;
        Reagent = reagent;
        Method = method;
        Scale = scale;
    }
}
