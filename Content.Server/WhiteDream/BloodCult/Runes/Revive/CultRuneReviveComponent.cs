using Content.Shared.Damage;
using Content.Shared.FixedPoint;

namespace Content.Server.WhiteDream.BloodCult.Runes.Revive;

[RegisterComponent]
public sealed partial class CultRuneReviveComponent : Component
{
    [DataField]
    public float ReviveRange = 0.5f;

    [DataField]
    public DamageSpecifier Healing = new()
    {
        DamageDict = new() // Forge-Change: ключом стал ProtoId<DamageTypePrototype>
        {
            ["Brute"] = -100,
            ["Burn"] = -100,
            ["Heat"] = -100,
            ["Asphyxiation"] = -100,
            ["Bloodloss"] = -100,
            ["Poison"] = -50,
            ["Cellular"] = -50
        }
    };
}
