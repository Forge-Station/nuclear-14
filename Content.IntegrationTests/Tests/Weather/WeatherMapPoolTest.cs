using Content.Server.Maps;
using Content.Shared.Weather;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Weather;

[TestFixture]
public sealed class WeatherMapPoolTest
{
    [TestCase("Sunnyvale", false, false, false, 8)]
    [TestCase("Wendover", false, true, false, 11)]
    [TestCase("Yuma", false, true, false, 11)]
    [TestCase("Juneau", true, false, true, 13)]
    public async Task ConfiguredMapsUseRequestedWeather(string mapId, bool snow, bool sandstorm, bool hail, int expectedCount)
    {
        await using var pair = await PoolManager.GetServerClient();
        var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();

        await pair.Server.WaitAssertion(() =>
        {
            var map = prototypes.Index<GameMapPrototype>(mapId);
            var persisted = map.Persistence(map.MapPath);
            var count = 0;

            foreach (var weather in prototypes.EnumeratePrototypes<WeatherPrototype>())
            {
                var allowed = weather.ID != "Default";
                if (weather.ID.StartsWith("Snowfall", StringComparison.Ordinal))
                    allowed = snow;
                else if (weather.ID.StartsWith("Sandstorm", StringComparison.Ordinal))
                    allowed = sandstorm;
                else if (weather.ID == "Hail")
                    allowed = hail;

                var weight = map.GetWeatherWeight(weather);
                Assert.That(weight, Is.EqualTo(allowed ? 1 : 0), $"{mapId}: {weather.ID}");
                Assert.That(persisted.GetWeatherWeight(weather), Is.EqualTo(weight), "Persistence must retain weather settings");
                if (weight > 0)
                    count++;
            }

            Assert.That(count, Is.EqualTo(expectedCount));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task UnconfiguredMapRetainsGlobalWeatherWeights()
    {
        await using var pair = await PoolManager.GetServerClient();
        var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
        ProtoId<GameMapPrototype> mapId = "MercerIsland";

        await pair.Server.WaitAssertion(() =>
        {
            var map = prototypes.Index(mapId);
            Assert.That(map.WeatherWeights, Is.Empty);
            foreach (var weather in prototypes.EnumeratePrototypes<WeatherPrototype>())
                Assert.That(map.GetWeatherWeight(weather), Is.EqualTo(Math.Max(0, weather.Chance)), weather.ID);
        });

        await pair.CleanReturnAsync();
    }
}
