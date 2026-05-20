using Content.Shared._NC.Trade;
using Robust.Shared.Random;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private const int SoftFairThreshold = 8;
    private const int SoftRewardThreshold = 512;
    private const int MaxRngCache = 4096;

    private const int RngEvictChunk = 256;
    private const double SoftFairHeatPenalty = 0.75;
    private const double SoftFairRepeatPenalty = 0.45;
    private const double SoftFairStreakPenalty = 0.2;
    private const double SoftFairMinWeight = 0.04;
    private const double SoftFairDecay = 0.55;

    private readonly Queue<QuasiKey> _quasiPhaseOrder = new();
    private readonly Dictionary<QuasiKey, SoftFairState> _softFair = new();
    private readonly Queue<QuasiKey> _softFairOrder = new();

    private double NextUnit() => _random.NextFloat();

    private static bool TryNormalizeRange(
        IntRange range,
        int minClamp,
        int maxClamp,
        out int min,
        out int buckets)
    {
        var a = range.Min;
        var b = range.Max;

        if (b < a)
            (a, b) = (b, a);

        a = Math.Clamp(a, minClamp, maxClamp);
        b = Math.Clamp(b, minClamp, maxClamp);

        if (b <= a)
        {
            min = a;
            buckets = 1;
            return false;
        }

        min = a;
        buckets = b - a + 1;
        return true;
    }

    private int RollSmooth(in QuasiKey key, int min, int buckets, double jitter)
    {
        var existing = _quasiPhase.TryGetValue(key, out var p);
        if (!existing)
        {
            if (_quasiPhase.Count >= MaxRngCache)
                EvictQuasiPhaseOldest(RngEvictChunk);

            p = NextUnit();
            _quasiPhaseOrder.Enqueue(key);
        }

        var j = (NextUnit() - 0.5) * 2.0 * jitter;

        p = p + Golden + j;
        p -= Math.Floor(p);
        _quasiPhase[key] = p;

        var idx = (int) Math.Floor(p * buckets);
        if ((uint) idx >= (uint) buckets)
            idx = buckets - 1;

        return min + idx;
    }

    private int RollFair(
        QuasiKey key,
        IntRange range,
        int minClamp,
        int maxClamp = int.MaxValue,
        double jitter = DefaultJitter)
    {
        if (!TryNormalizeRange(range, minClamp, maxClamp, out var min, out var buckets))
            return min;

        return buckets <= SoftFairThreshold
            ? RollSoftFair(key, min, buckets)
            : RollSmooth(key, min, buckets, jitter);
    }

    private int RollSoft(QuasiKey key, IntRange range, int minClamp, int maxClamp = int.MaxValue)
    {
        if (!TryNormalizeRange(range, minClamp, maxClamp, out var min, out var buckets))
            return min;

        return buckets <= SoftRewardThreshold
            ? RollSoftFair(key, min, buckets)
            : min + _random.Next(buckets);
    }

    private int RollSoftFair(QuasiKey key, int min, int buckets)
    {
        var existing = _softFair.TryGetValue(key, out var state);
        if (!existing)
        {
            if (_softFair.Count >= MaxRngCache)
                EvictSoftFairOldest(RngEvictChunk);

            state = new();
            _softFair[key] = state;
            _softFairOrder.Enqueue(key);
        }

        var max = min + buckets - 1;

        var needsReset =
            state!.Min != min ||
            state.Max != max ||
            state.Heat.Count != buckets;

        if (needsReset)
        {
            state.Min = min;
            state.Max = max;
            state.LastIdx = -1;
            state.Streak = 0;
            state.Heat.Clear();
            for (var i = 0; i < buckets; i++)
                state.Heat.Add(0);
        }

        Span<double> weights = stackalloc double[buckets];
        var total = 0.0;
        for (var i = 0; i < buckets; i++)
        {
            var weight = 1.0 / (1.0 + state.Heat[i] * SoftFairHeatPenalty);
            if (i == state.LastIdx)
                weight *= state.Streak > 1 ? SoftFairStreakPenalty : SoftFairRepeatPenalty;

            weight = Math.Max(weight, SoftFairMinWeight);
            weights[i] = weight;
            total += weight;
        }

        var idx = 0;
        var roll = _random.NextDouble() * total;
        for (var i = 0; i < buckets; i++)
        {
            roll -= weights[i];
            if (roll > 0)
                continue;

            idx = i;
            break;
        }

        for (var i = 0; i < state.Heat.Count; i++)
            state.Heat[i] *= SoftFairDecay;

        state.Heat[idx] += 1.0;
        state.Streak = idx == state.LastIdx ? state.Streak + 1 : 1;
        state.LastIdx = idx;

        return min + idx;
    }

    private void EvictQuasiPhaseOldest(int count)
    {
        var evicted = 0;
        while (evicted < count && _quasiPhaseOrder.Count > 0)
        {
            var oldest = _quasiPhaseOrder.Dequeue();
            if (_quasiPhase.Remove(oldest))
                evicted++;
        }

        if (evicted == 0 && _quasiPhase.Count >= MaxRngCache)
        {
            _quasiPhase.Clear();
            _quasiPhaseOrder.Clear();
        }
    }

    private void EvictSoftFairOldest(int count)
    {
        var evicted = 0;
        while (evicted < count && _softFairOrder.Count > 0)
        {
            var oldest = _softFairOrder.Dequeue();
            if (_softFair.Remove(oldest))
                evicted++;
        }

        if (evicted == 0 && _softFair.Count >= MaxRngCache)
        {
            _softFair.Clear();
            _softFairOrder.Clear();
        }
    }

    private void ClearRngCachesInternal()
    {
        _quasiPhase.Clear();
        _quasiPhaseOrder.Clear();
        _softFair.Clear();
        _softFairOrder.Clear();
    }

    private static T PickWeighted<T>(
        IRobustRandom random,
        IReadOnlyList<T> list,
        Func<T, int> weightSelector)
    {
        if (list.Count == 0)
            throw new InvalidOperationException("PickWeighted called with empty list.");

        long total = 0;

        var weights = list.Count <= 128
            ? stackalloc int[list.Count]
            : new int[list.Count];

        for (var i = 0; i < list.Count; i++)
        {
            var w = weightSelector(list[i]);
            if (w < 0)
                w = 0;

            weights[i] = w;
            total += w;
        }

        if (total <= 0)
            return list[random.Next(list.Count)];

        var r = total <= int.MaxValue
            ? random.Next((int) total)
            : (long) (random.NextDouble() * total);

        long acc = 0;
        for (var i = 0; i < list.Count; i++)
        {
            var w = weights[i];
            if (w <= 0)
                continue;

            acc += w;
            if (r < acc)
                return list[i];
        }

        for (var i = list.Count - 1; i >= 0; i--)
            if (weights[i] > 0)
                return list[i];

        return list[^1];
    }
}
