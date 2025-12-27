namespace Content.Server._NC.Trade;

public sealed partial class NcStoreLogicSystem
{

    private bool TryTakeCurrency(EntityUid user, string stackType, int amount)
    {
        return _currencyService.TryTakeCurrency(user, stackType, amount);
    }

    public void GiveCurrency(EntityUid user, string stackType, int amount)
    {
        _currencyService.GiveCurrency(user, stackType, amount);
    }
}
