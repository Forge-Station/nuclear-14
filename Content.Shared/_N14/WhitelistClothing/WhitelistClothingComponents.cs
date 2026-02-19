namespace Content.Shared.WhitelistClothing.Components;

[RegisterComponent]
public sealed partial class WhitelistClothingComponent : Component
{
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public string Whitelist = "SupermutantArmor";
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public string Slot = "outerclothing";
    [DataField(required: true), ViewVariables(VVAccess.ReadWrite)]
    public string WhitelistState = "humanoid";
}
