using Content.Shared._NC.Trade;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;


namespace Content.Server._NC.Trade;

public sealed partial class NcStoreLogicSystem
{
    private PrototypeMatchMode ResolveMatchMode(string expectedProtoId, PrototypeMatchMode configured)
    {
        if (configured == PrototypeMatchMode.Descendants)
            return PrototypeMatchMode.Descendants;

        if (_protos.TryIndex<EntityPrototype>(expectedProtoId, out var expectedProto) && expectedProto.Abstract)
            return PrototypeMatchMode.Descendants;

        return PrototypeMatchMode.Exact;
    }

    private string? GetProductStackType(string productProtoId)
    {
        if (_productStackTypeCache.TryGetValue(productProtoId, out var cached))
            return cached;

        string? stackType = null;

        if (_protos.TryIndex<EntityPrototype>(productProtoId, out var expectedProto))
        {
            var stackName = _compFactory.GetComponentName(typeof(StackComponent));
            if (expectedProto.TryGetComponent(stackName, out StackComponent? prodStackDef))
                stackType = prodStackDef.StackTypeId;
        }

        _productStackTypeCache[productProtoId] = stackType;
        return stackType;
    }

    private string[] GetProtoAndAncestors(EntityPrototype proto)
    {
        var id = proto.ID;
        if (_protoAndAncestorsCache.TryGetValue(id, out var cached))
            return cached;

        var visited = new HashSet<string>();
        var result = new List<string>();
        var stack = new Stack<string>();
        stack.Push(id);

        while (stack.Count > 0)
        {
            var cur = stack.Pop();
            if (!visited.Add(cur))
                continue;
            result.Add(cur);

            if (!_protos.TryIndex<EntityPrototype>(cur, out var curProto) || curProto.Parents == null)
                continue;

            foreach (var p in curProto.Parents)
                if (!string.IsNullOrWhiteSpace(p))
                    stack.Push(p);
        }

        var arr = result.ToArray();
        _protoAndAncestorsCache[id] = arr;
        return arr;
    }


    private bool IsProtoOrDescendant(EntityPrototype candidate, string expectedId)
    {
        if (candidate.ID == expectedId)
            return true;

        var ancestors = GetProtoAndAncestors(candidate);
        foreach (var t in ancestors)
            if (t == expectedId)
                return true;

        return false;
    }
}
