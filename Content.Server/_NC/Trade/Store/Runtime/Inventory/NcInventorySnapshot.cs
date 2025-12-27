namespace Content.Server._NC.Trade;

public sealed class NcInventorySnapshot
{
    public readonly Dictionary<string, int> AncestorCounts = new();
    public readonly Dictionary<string, int> ProtoCounts = new();
    public readonly Dictionary<string, int> StackTypeCounts = new();

    public void Clear()
    {
        ProtoCounts.Clear();
        AncestorCounts.Clear();
        StackTypeCounts.Clear();
    }
}
