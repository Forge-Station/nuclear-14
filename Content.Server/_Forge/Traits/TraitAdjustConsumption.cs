using Content.Shared._Forge.Nutrition;
using Content.Shared.Traits;
using JetBrains.Annotations;
using Robust.Shared.Serialization.Manager;

namespace Content.Server._Forge.Traits;

/// <summary>
///     Multiplies hunger and thirst decay. Stacks with other traits that use the same component,
///     instead of dropping the second one because the component already exists.
/// </summary>
[UsedImplicitly]
public sealed partial class TraitAdjustConsumption : TraitFunction
{
    [DataField]
    public float HungerMultiplier = 1f;

    [DataField]
    public float ThirstMultiplier = 1f;

    public override void OnPlayerSpawn(
        EntityUid uid,
        IComponentFactory factory,
        IEntityManager entityManager,
        ISerializationManager serializationManager)
    {
        if (!entityManager.TryGetComponent<N14ConsumptionModifierComponent>(uid, out var comp))
        {
            comp = new N14ConsumptionModifierComponent();
            entityManager.AddComponent(uid, comp);
        }

        comp.HungerMultiplier *= HungerMultiplier;
        comp.ThirstMultiplier *= ThirstMultiplier;
        entityManager.Dirty(uid, comp);
    }
}
