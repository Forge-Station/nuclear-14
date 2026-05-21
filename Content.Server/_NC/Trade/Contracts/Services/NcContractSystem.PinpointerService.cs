namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem
{
    private readonly IContractPinpointerRegistry _pinpointerService = new ContractPinpointerService();

    private sealed class ContractPinpointerService : IContractPinpointerRegistry
    {
        public List<EntityUid> ObjectivePinpointersScratch { get; } = new();
        public List<EntityUid> RetrievalPulledCargoScratch { get; } = new();

        public void RegisterIssuedPinpointer(
            IContractObjectiveRuntimeStore runtime,
            (EntityUid Store, string ContractId) key,
            ObjectiveRuntimeState state,
            EntityUid user,
            EntityUid pinpointer
        )
        {
            state.PinpointerEntities.Add(pinpointer);
            runtime.ByPinpointer[pinpointer] = key;
            runtime.PinpointerOwners[pinpointer] = user;
        }

        public void UnregisterIssuedPinpointer(
            IContractObjectiveRuntimeStore runtime,
            EntityUid pinpointer,
            (EntityUid Store, string ContractId) key
        )
        {
            runtime.ByPinpointer.Remove(pinpointer);
            runtime.PinpointerOwners.Remove(pinpointer);

            if (runtime.ByContract.TryGetValue(key, out var state))
                state.PinpointerEntities.Remove(pinpointer);
        }

        public bool TryGetOwner(IContractObjectiveRuntimeStore runtime, EntityUid pinpointer, out EntityUid owner) =>
            runtime.PinpointerOwners.TryGetValue(pinpointer, out owner);
    }
}
