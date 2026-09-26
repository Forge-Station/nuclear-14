using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.FactionResearch;

[NetSerializable, Serializable]
public enum FactionResearchUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class FactionResearchPrintMessage : BoundUserInterfaceMessage
{
    public string PrintId;

    public FactionResearchPrintMessage(string printId)
    {
        PrintId = printId;
    }
}

[Serializable, NetSerializable]
public sealed class FactionResearchConvertMessage : BoundUserInterfaceMessage
{
    public NetEntity Item;

    public FactionResearchConvertMessage(NetEntity item)
    {
        Item = item;
    }
}

[Serializable, NetSerializable]
public sealed class FactionResearchWithdrawDiskMessage : BoundUserInterfaceMessage
{
    public int Amount;

    public FactionResearchWithdrawDiskMessage(int amount)
    {
        Amount = amount;
    }
}

[Serializable, NetSerializable]
public sealed class FactionResearchPrintState
{
    public string Id = default!;
    public string Name = default!;
    public string Category = default!;
    public int Tier;
    public int Cost;
    public bool Available;
}

[Serializable, NetSerializable]
public sealed class FactionResearchConvertState
{
    public NetEntity Item;
    public string Name = default!;
    public int Points;
}

[Serializable, NetSerializable]
public sealed class FactionResearchBoundInterfaceState : BoundUserInterfaceState
{
    public string Faction = default!;
    public int Points;
    public bool CanCraft;
    public List<FactionResearchPrintState> Prints = new();
    public List<FactionResearchConvertState> Convertibles = new();
}
