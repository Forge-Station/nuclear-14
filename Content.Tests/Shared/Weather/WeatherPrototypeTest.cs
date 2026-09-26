using Content.Shared.Weather;
using NUnit.Framework;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;

namespace Content.Tests.Shared.Weather;

[TestFixture]
[TestOf(typeof(WeatherPrototype))]
public sealed class WeatherPrototypeTest : ContentUnitTest
{
    private static readonly ProtoId<WeatherPrototype> ClearWeatherId = "TestClearWeather";
    private static readonly ProtoId<WeatherPrototype> RadioactiveWeatherId = "TestRadioactiveWeather";
    private IPrototypeManager _prototypes = default!;

    [OneTimeSetUp]
    public void SetupPrototypes()
    {
        IoCManager.Resolve<ISerializationManager>().Initialize();
        _prototypes = IoCManager.Resolve<IPrototypeManager>();
        _prototypes.Initialize();
        _prototypes.LoadString(@"
- type: weather
  id: TestClearWeather

- type: weather
  id: TestRadioactiveWeather
  radioactive: true
  rads: 2
  duration: 120
  chance: 0
  visibilityClearRadius: 8
  visibilityClearBuffer: 2

");
        _prototypes.ResolveResults();
    }

    [Test]
    public void ExistingWeatherDoesNotGainRadiationOrVisibilityRestrictions()
    {
        var weather = _prototypes.Index(ClearWeatherId);
        Assert.That(weather.Radioactive, Is.False);
        Assert.That(weather.VisibilityClearRadius, Is.Zero);
        Assert.That(weather.VisibilityClearBuffer, Is.EqualTo(1f));
    }

    [Test]
    public void WeatherEffectsAndRandomPoolExclusionLoad()
    {
        var weather = _prototypes.Index(RadioactiveWeatherId);
        Assert.That(weather.Radioactive, Is.True);
        Assert.That(weather.RadsPerSecond, Is.EqualTo(2f));
        Assert.That(weather.Duration, Is.EqualTo(120f));
        Assert.That(weather.VisibilityClearRadius, Is.EqualTo(8f));
        Assert.That(weather.VisibilityClearBuffer, Is.EqualTo(2f));
        Assert.That(weather.Chance, Is.Zero);
    }
}
