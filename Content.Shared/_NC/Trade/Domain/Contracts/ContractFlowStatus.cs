using Robust.Shared.Serialization;

namespace Content.Shared._NC.Trade;

[Serializable, NetSerializable]
public enum ContractFlowStatus : byte
{
    Available = 0,
    AwaitingActivation = 1,
    InProgress = 2,
    ReadyToTurnIn = 3,
    Failed = 4
}
