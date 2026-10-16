using Robust.Shared.Prototypes;
namespace Content.Shared.Abilities.Psionics
{
    [RegisterComponent]
    public sealed partial class PsionicInvisibilityUsedComponent : Component
    {
        public const string PsionicInvisibilityUsedActionPrototype = "ActionPsionicInvisibilityUsed";
        [DataField("psionicInvisibilityUsedActionId")]
        public string? PsionicInvisibilityUsedActionId = "ActionPsionicInvisibilityUsed";

        [DataField("psionicInvisibilityUsedActionEntity")]
        public EntityUid? PsionicInvisibilityUsedActionEntity;
    }
}
