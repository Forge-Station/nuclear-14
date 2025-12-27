using Content.Shared.Stacks;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private bool TryGetStackTypeId(string productProtoId, out string stackTypeId)
    {
        stackTypeId = string.Empty;

        if (!_prototypes.TryIndex<EntityPrototype>(productProtoId, out var expectedProto))
            return false;

        if (!expectedProto.TryGetComponent("Stack", out StackComponent? prodStackDef))
            return false;

        if (string.IsNullOrWhiteSpace(prodStackDef.StackTypeId))
            return false;

        stackTypeId = prodStackDef.StackTypeId;
        return true;
    }

    private int GetProtoDepth(string protoId)
    {
        if (_depthCache.TryGetValue(protoId, out var d))
        {
                return 0;

            return d;
        }

        if (!_prototypes.TryIndex<EntityPrototype>(protoId, out var proto))
        {
            _depthCache[protoId] = 0;
            return 0;
        }
        _depthCache[protoId] = -1;

        var best = 0;
        var parents = proto.Parents;
        if (parents is { Length: > 0, })
        {
            foreach (var p in parents)
            {
                var pd = GetProtoDepth(p) + 1;
                if (pd > best)
                    best = pd;
            }
        }

        _depthCache[protoId] = best;
        return best;
    }

    private List<string> GetAncestorsInclusive(string protoId)
    {
        if (_ancestorsCache.TryGetValue(protoId, out var list))
            return list;

        var result = new List<string> { protoId, };

        if (_prototypes.TryIndex<EntityPrototype>(protoId, out var proto))
        {
            var parents0 = proto.Parents;
            if (parents0 is { Length: > 0, })
            {
                var stack = new Stack<string>(parents0);
                var seen = new HashSet<string>();

                while (stack.Count > 0)
                {
                    var cur = stack.Pop();
                    if (!seen.Add(cur))
                        continue;

                    result.Add(cur);

                    if (_prototypes.TryIndex<EntityPrototype>(cur, out var p))
                    {
                        var parents = p.Parents;
                        if (parents is { Length: > 0, })
                        {
                            foreach (var t in parents)
                                stack.Push(t);
                        }
                    }
                }
            }
        }

        _ancestorsCache[protoId] = result;
        return result;
    }


    /// <summary>
    ///     Returns true if <paramref name="childProtoId"/> is the same as <paramref name="ancestorProtoId"/> or inherits from it.
    ///     Uses cached ancestor lists to avoid per-call Stack/HashSet allocations.
    /// </summary>
    private bool IsProtoOrDescendant(string childProtoId, string ancestorProtoId)
    {
        if (childProtoId == ancestorProtoId)
            return true;

        var ancestors = GetAncestorsInclusive(childProtoId);
        for (var i = 0; i < ancestors.Count; i++)
        {
            if (ancestors[i] == ancestorProtoId)
                return true;
        }

        return false;
    }
}
