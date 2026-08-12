using Content.Shared._Forge.Warfront;

namespace Content.Shared._Forge.Warfront.FactionPoints;

public sealed record FactionPointsChangedEvent(WarfrontFaction Faction, int NewBalance);
