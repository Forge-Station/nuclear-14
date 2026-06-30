using Content.Server.CharacterAppearance.Components;
using Content.Shared.Humanoid;
using Content.Shared.Preferences;

namespace Content.Server.Humanoid.Systems;

public sealed class RandomHumanoidAppearanceSystem : EntitySystem
{
    [Dependency] private readonly HumanoidAppearanceSystem _humanoid = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RandomHumanoidAppearanceComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(EntityUid uid, RandomHumanoidAppearanceComponent component, MapInitEvent args)
    {
        // If we have an initial profile/base layer set, do not randomize this humanoid.
        if (!TryComp(uid, out HumanoidAppearanceComponent? humanoid) || !string.IsNullOrEmpty(humanoid.Initial))
        {
            return;
        }

        var profile = HumanoidCharacterProfile.RandomWithSpecies(humanoid.Species);

        if (component.Sex is { } sex)
        {
            profile = profile.WithSex(sex);

            if (SharedHumanoidAppearanceSystem.DefaultSexVoice.TryGetValue(sex, out var voice))
                profile = profile.WithVoice(voice);
        }

        if (component.Gender is { } gender)
            profile = profile.WithGender(gender);

        if (component.Age is { } age)
            profile = profile.WithAge(Math.Max(0, age));

        if (component.SkinColor is { } skinColor)
            profile = profile.WithCharacterAppearance(profile.Appearance.WithSkinColor(skinColor));

        if (!string.IsNullOrWhiteSpace(component.Hair))
            profile = profile.WithCharacterAppearance(profile.Appearance.WithHairStyleName(component.Hair));

        if (component.HairColor is { } hairColor)
            profile = profile.WithCharacterAppearance(profile.Appearance.WithHairColor(hairColor));

        _humanoid.LoadProfile(uid, profile, humanoid);

        if (component.RandomizeName)
            _metaData.SetEntityName(uid, profile.Name);
    }
}
