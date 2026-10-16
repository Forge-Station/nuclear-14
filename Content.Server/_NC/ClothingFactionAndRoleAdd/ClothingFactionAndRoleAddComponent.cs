using Content.Shared.Roles;
using Content.Shared.NPC.Prototypes;


namespace Content.Server._NC.ClothingFactionAndRoleAdd
{
    [RegisterComponent]
    /// <summary>
    /// allows you to add fractions and roles.
    /// </summary>
    public sealed partial class ClothingFactionAndRoleAddComponent : Component
    {
        public bool IsActive = false;

        /// <summary>
        /// ID of the fraction that needs to be added or removed when the corresponding clothing is equipped or removed.
        /// </summary>
        [ViewVariables(VVAccess.ReadWrite),
         DataField("faction")]
        public string Faction = default!;

        /// <summary>
        /// ID of the role (job) that needs to be added or removed
        /// for the wearer when the appropriate clothing is equipped or removed.
        /// If you do not specify a role, then only a faction will be added to the player.
        /// </summary>
        [ViewVariables(VVAccess.ReadWrite),
         DataField("role")]
        public string JobId = default!;
    }
}
