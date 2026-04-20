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

}

