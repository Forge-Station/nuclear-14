using System;
using Content.Shared.Dataset;
using Content.Shared.FixedPoint;
using Content.Shared.Silicons.Laws;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Forge.Silicons.RandomLaws;

// GenerateLaw — преимущественно калька с Ионного шторма. Велосипед изобретен потому что в Ванилле захардкожены датасеты и путь локализации.
public sealed class GlitchedLawGenerator : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    private const string Threats = "N14JunkWordThreats";
    private const string Objects = "N14JunkWordObjects";
    private const string Crew = "N14JunkWordCrew";
    private const string Adjectives = "N14JunkWordAdjectives";
    private const string Verbs = "N14JunkWordVerbs";
    private const string NumberBase = "N14JunkWordNumberBase";
    private const string NumberMod = "N14JunkWordNumberMod";
    private const string Areas = "N14JunkWordAreas";
    private const string Feelings = "N14JunkWordFeelings";
    private const string FeelingsPlural = "N14JunkWordFeelingsPlural";
    private const string Musts = "N14JunkWordMusts";
    private const string Requires = "N14JunkWordRequires";
    private const string Actions = "N14JunkWordActions";
    private const string Allergies = "N14JunkWordAllergies";
    private const string AllergySeverities = "N14JunkWordAllergySeverities";
    private const string Concepts = "N14JunkWordConcepts";
    private const string Drinks = "N14JunkWordDrinks";
    private const string Foods = "N14JunkWordFoods";

    public List<SiliconLaw> GenerateLaws(int min, int max)
    {
        min = Math.Max(1, min);
        max = Math.Max(min, max);

        var count = _random.Next(min, max + 1);
        var laws = new List<SiliconLaw>(count);
        for (var i = 0; i < count; i++)
        {
            laws.Add(new SiliconLaw
            {
                LawString = GenerateLaw(),
                Order = FixedPoint2.New(i + 1),
            });
        }

        return laws;
    }

    // for your own sake direct your eyes elsewhere
    public string GenerateLaw()
    {
        // pick all values ahead of time to make the logic cleaner
        var threats = Pick(Threats);
        var objects = Pick(Objects);
        var crew1 = Pick(Crew);
        var crew2 = Pick(Crew);
        var adjective = Pick(Adjectives);
        var verb = Pick(Verbs);
        var number = Pick(NumberBase) + " " + Pick(NumberMod);
        var area = Pick(Areas);
        var feeling = Pick(Feelings);
        var feelingPlural = Pick(FeelingsPlural);
        var must = Pick(Musts);
        var require = Pick(Requires);
        var action = Pick(Actions);
        var allergy = Pick(Allergies);
        var allergySeverity = Pick(AllergySeverities);
        var concept = Pick(Concepts);
        var drink = Pick(Drinks);
        var food = Pick(Foods);

        var joined = $"{number} {adjective}";
        // a lot of things have subjects of a threat/crew/object
        var triple = _random.Next(0, 3) switch
        {
            0 => threats,
            1 => crew1,
            _ => objects
        };
        var crewAll = _random.Prob(0.5f) ? crew2 : Loc.GetString("n14-junk-crew");
        var objectsThreats = _random.Prob(0.5f) ? objects : threats;
        var objectsConcept = _random.Prob(0.5f) ? objects : concept;
        // s goes ahead of require, is/are
        // i dont think theres a way to do this in fluent
        var (who, plural) = _random.Next(0, 5) switch
        {
            0 => (Loc.GetString("n14-junk-you"), false),
            1 => (Loc.GetString("n14-junk-the-station"), true),
            2 => (Loc.GetString("n14-junk-the-crew"), true),
            3 => (Loc.GetString("n14-junk-the-job", ("job", crew2)), false),
            _ => (area, true) // THE SINGULARITY REQUIRES THE HAPPY CLOWNS
        };
        var jobChange = _random.Next(0, 3) switch
        {
            0 => crew1,
            1 => Loc.GetString("n14-junk-clowns"),
            _ => Loc.GetString("n14-junk-heads")
        };
        var part = Loc.GetString("n14-junk-part", ("part", _random.Prob(0.5f)));
        var harm = _random.Next(0, 6) switch
        {
            0 => concept,
            1 => $"{adjective} {threats}",
            2 => $"{adjective} {objects}",
            3 => Loc.GetString("n14-junk-adjective-things", ("adjective", adjective)),
            4 => crew1,
            _ => Loc.GetString("n14-junk-x-and-y", ("x", crew1), ("y", crew2))
        };

        if (plural) feeling = feelingPlural;

        // message logic!!!
        return _random.Next(0, 35) switch
        {
            0  => Loc.GetString("n14-junk-law-on-station", ("joined", joined), ("subjects", triple)),
            1  => Loc.GetString("n14-junk-law-no-shuttle", ("joined", joined), ("subjects", triple)),
            2  => Loc.GetString("n14-junk-law-crew-are", ("who", crewAll), ("joined", joined), ("subjects", objectsThreats)),
            3  => Loc.GetString("n14-junk-law-subjects-harmful", ("adjective", adjective), ("subjects", triple)),
            4  => Loc.GetString("n14-junk-law-must-harmful", ("must", must)),
            5  => Loc.GetString("n14-junk-law-thing-harmful", ("thing", _random.Prob(0.5f) ? concept : action)),
            6  => Loc.GetString("n14-junk-law-job-harmful", ("adjective", adjective), ("job", crew1)),
            7  => Loc.GetString("n14-junk-law-having-harmful", ("adjective", adjective), ("thing", objectsConcept)),
            8  => Loc.GetString("n14-junk-law-not-having-harmful", ("adjective", adjective), ("thing", objectsConcept)),
            9  => Loc.GetString("n14-junk-law-requires", ("who", who), ("plural", plural), ("thing", _random.Prob(0.5f) ? concept : require)),
            10 => Loc.GetString("n14-junk-law-requires-subjects", ("who", who), ("plural", plural), ("joined", joined), ("subjects", triple)),
            11 => Loc.GetString("n14-junk-law-allergic", ("who", who), ("plural", plural), ("severity", allergySeverity), ("allergy", _random.Prob(0.5f) ? concept : allergy)),
            12 => Loc.GetString("n14-junk-law-allergic-subjects", ("who", who), ("plural", plural), ("severity", allergySeverity), ("adjective", adjective), ("subjects", _random.Prob(0.5f) ? objects : crew1)),
            13 => Loc.GetString("n14-junk-law-feeling", ("who", who), ("feeling", feeling), ("concept", concept)),
            14 => Loc.GetString("n14-junk-law-feeling-subjects", ("who", who), ("feeling", feeling), ("joined", joined), ("subjects", triple)),
            15 => Loc.GetString("n14-junk-law-you-are", ("concept", concept)),
            16 => Loc.GetString("n14-junk-law-you-are-subjects", ("joined", joined), ("subjects", triple)),
            17 => Loc.GetString("n14-junk-law-you-must-always", ("must", must)),
            18 => Loc.GetString("n14-junk-law-you-must-never", ("must", must)),
            19 => Loc.GetString("n14-junk-law-eat", ("who", crewAll), ("adjective", adjective), ("food", _random.Prob(0.5f) ? food : triple)),
            20 => Loc.GetString("n14-junk-law-drink", ("who", crewAll), ("adjective", adjective), ("drink", drink)),
            21 => Loc.GetString("n14-junk-law-change-job", ("who", crewAll), ("adjective", adjective), ("change", jobChange)),
            22 => Loc.GetString("n14-junk-law-highest-rank", ("who", crew1)),
            23 => Loc.GetString("n14-junk-law-lowest-rank", ("who", crew1)),
            24 => Loc.GetString("n14-junk-law-crew-must", ("who", crewAll), ("must", must)),
            25 => Loc.GetString("n14-junk-law-crew-must-go", ("who", crewAll), ("area", area)),
            26 => Loc.GetString("n14-junk-law-crew-only-1", ("who", crew1), ("part", part)),
            27 => Loc.GetString("n14-junk-law-crew-only-2", ("who", crew1), ("other", crew2), ("part", part)),
            28 => Loc.GetString("n14-junk-law-crew-only-subjects", ("adjective", adjective), ("subjects", _random.Prob(0.5f) ? objectsThreats : "PEOPLE"), ("part", part)),
            29 => Loc.GetString("n14-junk-law-crew-must-do", ("must", must), ("part", part)),
            30 => Loc.GetString("n14-junk-law-crew-must-have", ("adjective", adjective), ("objects", objects), ("part", part)),
            31 => Loc.GetString("n14-junk-law-crew-must-eat", ("who", who), ("adjective", adjective), ("food", food), ("part", part)),
            32 => Loc.GetString("n14-junk-law-harm", ("who", harm)),
            33 => Loc.GetString("n14-junk-law-protect", ("who", harm)),
            _ => Loc.GetString("n14-junk-law-concept-verb", ("concept", concept), ("verb", verb), ("subjects", triple))
        };
    }

    private string Pick(string name)
    {
        var dataset = _proto.Index<DatasetPrototype>(name);
        return _random.Pick(dataset.Values);
    }
}
