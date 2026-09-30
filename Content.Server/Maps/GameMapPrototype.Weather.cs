using Content.Shared.Weather;
using Robust.Shared.Prototypes;

namespace Content.Server.Maps;

public sealed partial class GameMapPrototype
{
    /// <summary>
    /// Overrides random weather weights for this map. Zero disables an event;
    /// omitted entries retain the weather prototype's global weight.
    /// These weights do not restrict the explicit admin weather command.
    /// </summary>
    [DataField]
    public Dictionary<ProtoId<WeatherPrototype>, int> WeatherWeights = new();

    public int GetWeatherWeight(WeatherPrototype weather)
    {
        return Math.Max(0, WeatherWeights.GetValueOrDefault(weather.ID, weather.Chance));
    }
}
