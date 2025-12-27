using Content.Shared._NC.Trade;
using Robust.Shared.Random;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem : EntitySystem
{
    private double NextUnit() => _random.NextFloat();

    private int RollSmooth(
        QuasiKey key,
        IntRange range,
        int minClamp,
        int maxClamp = int.MaxValue,
        double jitter = DefaultJitter
    )
    {
        var min = range.Min;
        var max = range.Max;

        if (max < min)
            (min, max) = (max, min);

        min = Math.Clamp(min, minClamp, maxClamp);
        max = Math.Clamp(max, minClamp, maxClamp);

        if (max <= min)
            return min;

        if (_quasiPhase.Count > 4096)
            _quasiPhase.Clear();

        if (!_quasiPhase.TryGetValue(key, out var p))
            p = NextUnit();

        var j = (NextUnit() - 0.5) * 2.0 * jitter;
        p = p + Golden + j;
        p -= Math.Floor(p);
        _quasiPhase[key] = p;

        var buckets = max - min + 1;
        var idx = (int) Math.Floor(p * buckets);
        if (idx >= buckets)
            idx = buckets - 1;

        return min + idx;
    }

    private static T PickWeighted<T>(IRobustRandom random, IReadOnlyList<T> list, Func<T, int> weightSelector)
    {
        if (list.Count == 0)
            return default!;

        var weights = list.Count <= 128
            ? stackalloc int[list.Count]
            : new int[list.Count];

        var total = 0;
        for (var i = 0; i < list.Count; i++)
        {
            var w = weightSelector(list[i]);
            if (w <= 0)
                w = 1;

            weights[i] = w;
            total += w;
        }

        if (total <= 0)
            return list[random.Next(list.Count)];

        var value = random.Next(total);
        var accum = 0;
        for (var i = 0; i < list.Count; i++)
        {
            accum += weights[i];
            if (value < accum)
                return list[i];
        }

        return list[^1];
    }
}
