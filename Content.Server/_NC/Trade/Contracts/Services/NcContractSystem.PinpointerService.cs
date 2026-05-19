namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem
{
    private readonly ContractPinpointerService _pinpointerService = new();

    private sealed class ContractPinpointerService
    {
        public readonly List<EntityUid> ObjectivePinpointersScratch = new();
        public readonly List<EntityUid> RetrievalPulledCargoScratch = new();

        public void RegisterIssuedPinpointer(
            ContractObjectiveRuntimeService runtime,
            (EntityUid Store, string ContractId) key,
            ObjectiveRuntimeState state,
            EntityUid user,
            EntityUid pinpointer)
        {
            state.PinpointerEntities.Add(pinpointer);
            runtime.ByPinpointer[pinpointer] = key;
            runtime.PinpointerOwners[pinpointer] = user;
        }

        public void UnregisterIssuedPinpointer(
            ContractObjectiveRuntimeService runtime,
            EntityUid pinpointer,
            (EntityUid Store, string ContractId) key)
        {
            runtime.ByPinpointer.Remove(pinpointer);
            runtime.PinpointerOwners.Remove(pinpointer);

            if (runtime.ByContract.TryGetValue(key, out var state))
                state.PinpointerEntities.Remove(pinpointer);
        }

        public bool TryGetOwner(ContractObjectiveRuntimeService runtime, EntityUid pinpointer, out EntityUid owner)
        {
            return runtime.PinpointerOwners.TryGetValue(pinpointer, out owner);
        }
    }
}
