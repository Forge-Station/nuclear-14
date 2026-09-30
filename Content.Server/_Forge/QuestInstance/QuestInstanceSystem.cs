using Content.Server.Atmos.EntitySystems;
using Content.Server.Parallax;
using Content.Server.Weather;
using Content.Shared._Forge.QuestInstance;
using Content.Shared.Interaction;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Popups;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;


namespace Content.Server._Forge.QuestInstance;


public sealed partial class QuestInstanceSystem : EntitySystem
{
    [Dependency] private readonly AtmosphereSystem _atmos = default!;
    [Dependency] private readonly BiomeSystem _biome = default!;
    [Dependency] private readonly MapLoaderSystem _mapLoader = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly PullingSystem _pulling = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly WeatherSystem _weather = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<QuestBoardComponent, EntityTerminatingEvent>(OnBoardTerminating);
        SubscribeLocalEvent<QuestBoardComponent, AfterActivatableUIOpenEvent>(OnBoardUiOpened);
        SubscribeLocalEvent<QuestBoardComponent, QuestBoardSelectDifficultyMessage>(OnBoardSelectDifficulty);
        SubscribeLocalEvent<QuestSignpostComponent, InteractHandEvent>(OnSignpostUsed);
    }

    private static string DifficultyToPresetId(QuestDifficulty difficulty) =>
        difficulty switch
        {
            QuestDifficulty.Easy => "QuestInstanceEasy",
            QuestDifficulty.Medium => "QuestInstanceMedium",
            QuestDifficulty.Hard => "QuestInstanceHard",
            _ => "QuestInstanceEasy"
        };
}
