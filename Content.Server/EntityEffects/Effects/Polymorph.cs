using Content.Server.Polymorph.Components;
using Content.Server.Polymorph.Systems;
using Content.Shared.EntityEffects;
using Content.Shared.Polymorph;
using Robust.Shared.Prototypes;
using System.Linq; // Corvax-Change

namespace Content.Server.EntityEffects.Effects;

public sealed partial class Polymorph : LegacyEntityEffect
{
    /// <summary>
    ///     What polymorph prototype is used on effect
    /// </summary>
    [DataField("prototype")]
    public string PolymorphPrototype { get; set; }

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
    {
        // Corvax-Change-Start
        var polyProto = prototype.Index<PolymorphPrototype>(PolymorphPrototype);
        string entityDisplayName;

        if (!string.IsNullOrEmpty(polyProto.Configuration.Entity))
            entityDisplayName = prototype.Index<EntityPrototype>(polyProto.Configuration.Entity).Name;

        else if (polyProto.Configuration.RandomEnt != null && polyProto.Configuration.RandomEnt.Count > 0)
        {
            var names = polyProto.Configuration.RandomEnt
                .Where(entry => entry.PrototypeId != null)
                .Select(entry => prototype.Index<EntityPrototype>(entry.PrototypeId!).Name)
                .Distinct()
                .ToList();

            entityDisplayName = names.Count switch
            {
                0 => "unknown",
                1 => names[0],
                _ => string.Join(" / ", names) + " (Рандомно)"
            };
        }

        else
        {
            entityDisplayName = "unknown";
        }

        return Loc.GetString("reagent-effect-guidebook-make-polymorph",
            ("chance", Probability),
            ("entityname", entityDisplayName));
        // Corvax-Change-End
    }

    public override void Effect(EntityEffectBaseArgs args)
    {
        var entityManager = args.EntityManager;
        var uid = args.TargetEntity;
        var polySystem = entityManager.System<PolymorphSystem>();

        // Make it into a prototype
        entityManager.EnsureComponent<PolymorphableComponent>(uid);
        polySystem.PolymorphEntity(uid, PolymorphPrototype);
    }
}
