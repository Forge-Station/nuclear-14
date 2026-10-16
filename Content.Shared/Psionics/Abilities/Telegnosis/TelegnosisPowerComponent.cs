using Content.Shared.Actions;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;


namespace Content.Shared.Abilities.Psionics
{
    [RegisterComponent]
    public sealed partial class TelegnosisPowerComponent : Component
    {
        [DataField("prototype")]
        public string Prototype = "MobObserverTelegnostic";
        public InstantActionComponent? TelegnosisPowerAction = null;
        public const string TelegnosisActionPrototype = "ActionTelegnosis";
        [DataField("telegnosisActionId")]
        public string? TelegnosisActionId = "ActionTelegnosis";

        [DataField("telegnosisActionEntity")]
        public EntityUid? TelegnosisActionEntity;
    }
}