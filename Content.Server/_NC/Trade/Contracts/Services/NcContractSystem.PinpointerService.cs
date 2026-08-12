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
            EntityUid pinpointer
        )
        {
            state.PinpointerEntities.Add(pinpointer);
            runtime.ByPinpointer[pinpointer] = key;
        }

        public void UnregisterIssuedPinpointer(
            IContractObjectiveRuntimeStore runtime,
            EntityUid pinpointer,
            (EntityUid Store, string ContractId) key
        )
        {
            runtime.ByPinpointer.Remove(pinpointer);

            if (runtime.ByContract.TryGetValue(key, out var state))
                state.PinpointerEntities.Remove(pinpointer);
        }
    }
}
