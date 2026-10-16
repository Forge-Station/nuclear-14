using Content.Shared.Construction.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared.Preferences
{
    /// <summary>
    ///     Contains all player characters and the index of the currently selected character.
    ///     Serialized both over the network and to disk.
    /// </summary>
    [Serializable]
    [NetSerializable]
    public sealed class PlayerPreferences
    {
        private Dictionary<int, HumanoidCharacterProfile> _characters;

        public PlayerPreferences(IEnumerable<KeyValuePair<int, HumanoidCharacterProfile>> characters, int selectedCharacterIndex, Color adminOOCColor, List<ProtoId<ConstructionPrototype>> constructionFavorites)
            : this(characters, selectedCharacterIndex, adminOOCColor, constructionFavorites, Color.Transparent, Color.Transparent, string.Empty)
        {
        }

        // Forge-Change: sponsor cosmetic preferences (custom OOC/LOOC name color, chosen ghost skin)
        public PlayerPreferences(
            IEnumerable<KeyValuePair<int, HumanoidCharacterProfile>> characters,
            int selectedCharacterIndex,
            Color adminOOCColor,
            List<ProtoId<ConstructionPrototype>> constructionFavorites,
            Color sponsorOOCColor,
            Color sponsorLOOCColor,
            string sponsorGhostSkin)
        {
            _characters = new Dictionary<int, HumanoidCharacterProfile>(characters);
            SelectedCharacterIndex = selectedCharacterIndex;
            AdminOOCColor = adminOOCColor;
            ConstructionFavorites = constructionFavorites;
            SponsorOOCColor = sponsorOOCColor;
            SponsorLOOCColor = sponsorLOOCColor;
            SponsorGhostSkin = sponsorGhostSkin;
        }

        /// <summary>
        ///     All player characters.
        /// </summary>
        public IReadOnlyDictionary<int, HumanoidCharacterProfile> Characters => _characters;

        public HumanoidCharacterProfile GetProfile(int index)
        {
            return _characters[index];
        }

        /// <summary>
        ///     Index of the currently selected character.
        /// </summary>
        public int SelectedCharacterIndex { get; }

        /// <summary>
        ///     The currently selected character.
        /// </summary>
        public HumanoidCharacterProfile SelectedCharacter => Characters[SelectedCharacterIndex];

        public Color AdminOOCColor { get; set; }

        // Forge-Change-Start: sponsor cosmetics. Transparent color / empty skin mean "not set, use defaults".
        /// <summary>
        ///     Custom OOC name color chosen by a sponsor. <see cref="Color.Transparent"/> means unset.
        /// </summary>
        public Color SponsorOOCColor { get; set; }

        /// <summary>
        ///     Custom LOOC name color chosen by a sponsor. <see cref="Color.Transparent"/> means unset.
        /// </summary>
        public Color SponsorLOOCColor { get; set; }

        /// <summary>
        ///     Entity prototype id of the ghost skin chosen by a sponsor. Empty means default observer.
        /// </summary>
        public string SponsorGhostSkin { get; set; } = string.Empty;
        // Forge-Change-End

        /// <summary>
        ///    List of favorite items in the construction menu.
        /// </summary>
        public List<ProtoId<ConstructionPrototype>> ConstructionFavorites { get; set; } = [];

        public int IndexOfCharacter(HumanoidCharacterProfile profile)
        {
            return _characters.FirstOrNull(p => p.Value == profile)?.Key ?? -1;
        }

        public bool TryIndexOfCharacter(HumanoidCharacterProfile profile, out int index)
        {
            return (index = IndexOfCharacter(profile)) != -1;
        }
    }
}
