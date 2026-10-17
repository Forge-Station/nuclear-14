using System;
using Content.Shared.Preferences;

namespace Content.Client.Lobby
{
    public interface IClientPreferencesManager
    {
        event Action OnServerDataLoaded;

        bool ServerDataLoaded => Settings != null;

        GameSettings? Settings { get; }
        PlayerPreferences? Preferences { get; }
        void Initialize();
        void SelectCharacter(ICharacterProfile profile);
        void SelectCharacter(int slot);
        void UpdateCharacter(ICharacterProfile profile, int slot);
        void CreateCharacter(ICharacterProfile profile);
        void DeleteCharacter(ICharacterProfile profile);
        void DeleteCharacter(int slot);

        // Forge-Change: update sponsor cosmetic preferences (custom OOC/LOOC color, ghost skin).
        void UpdateSponsorPreferences(Color oocColor, Color loocColor, string ghostSkin);
    }
}
