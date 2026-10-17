using System.Text.RegularExpressions;

// Forge-Change: апстрим убрал этот помощник вместе со своей версией команд
// начисления времени. Форковые команды и панель переноса времени на нём держатся.
namespace Content.Server.Administration.Commands;

public sealed class PlayTimeCommandUtilities
{
    private readonly static Dictionary<string, int> Units = new() {
        { "y", 525960 },
        { "mo", 43800 },
        { "w", 10080 },
        { "d", 1440 },
        { "h", 60 },
        { "m", 1 },
    };

    public struct TimeUnit
    {
        public int TimeValue { get; }
        public string Unit { get; }

        public TimeUnit(int timeValue)
        {
            TimeValue = timeValue;
            Unit = "m";
        }

        public TimeUnit(int timeValue, string unit)
        {
            TimeValue = timeValue;
            Unit = unit;
        }
        public int ToMinutes()
        {
            var unitExists = Units.TryGetValue(Unit, out int minutes);

            if (!unitExists)
                return TimeValue;

            return TimeValue * minutes;
        }
    }

    public static List<TimeUnit> ConvertToTimeUnits(string timeString)
    {
        // Searching for something similar to 365d24h, etc.
        List<TimeUnit> result = new();

        // We want to support plain numbers as a translation to just minutes, just in case people don't know things like 30d or 1d are an option.
        if (int.TryParse(timeString, out int timeValue))
        {
            result.Add(new TimeUnit(timeValue, "m"));
            return result;
        }

        MatchCollection timeRegex = Regex.Matches(timeString, "(\\d+)([A-Za-z]+)");

        foreach (Match match in timeRegex)
        {
            bool isTimeAmountNumber = int.TryParse(match.Groups[1].Value, out int amountOfTime);
            string timeUnit = match.Groups[2].Value;

            if (!isTimeAmountNumber)
                continue;

            if (!Units.ContainsKey(timeUnit))
                continue;

            result.Add(new TimeUnit(amountOfTime, timeUnit));
        }

        return result;
    }

    public static int CountMinutes(string timeString)
    {
        List<TimeUnit> timeUnits = ConvertToTimeUnits(timeString);
        int total = 0;

        foreach (var timeUnit in timeUnits)
        {
            total += timeUnit.ToMinutes();
        }

        return total;
    }
}
