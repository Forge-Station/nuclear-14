using Robust.Shared.Prototypes;
using Content.Shared.Actions;
using Robust.Shared.Utility;

namespace Content.Server.Abilities.Felinid;

[RegisterComponent]
public sealed partial class FelinidComponent : Component
{
    /// <summary>
    /// The hairball prototype to use.
    /// </summary>
    [DataField("hairballPrototype")]
    public string HairballPrototype = "Hairball";

    //[DataField("hairballAction")]
    //public string HairballAction = "ActionHairball";

    [DataField("hairballActionId")]
    public string? HairballActionId = "ActionHairball";

    [DataField("hairballAction")]
    public EntityUid? HairballAction;

    [DataField("eatActionId")]
    public string? EatActionId = "ActionEatMouse";

    [DataField("eatAction")]
    public EntityUid? EatAction;

    [DataField("eatActionTarget")]
    public EntityUid? EatActionTarget = null;
}
