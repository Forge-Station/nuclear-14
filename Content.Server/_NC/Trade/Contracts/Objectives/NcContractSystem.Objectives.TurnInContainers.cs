using Content.Shared._NC.Trade;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem
{
    private readonly Dictionary<EntityUid, HashSet<string>> _turnInContainerGroupsByEntity = new();
    private readonly List<EntityUid> _turnInContainerQueryScratch = new();
    private readonly Dictionary<string, HashSet<EntityUid>> _turnInContainersByGroup = new(StringComparer.Ordinal);

    private void InitializeTurnInContainerIndex()
    {
        SubscribeLocalEvent<NcContractTurnInContainerComponent, ComponentStartup>(OnTurnInContainerStartup);
        SubscribeLocalEvent<NcContractTurnInContainerComponent, ComponentShutdown>(OnTurnInContainerShutdown);
    }

    private void ShutdownTurnInContainerIndex()
    {
        _turnInContainersByGroup.Clear();
        _turnInContainerGroupsByEntity.Clear();
        _turnInContainerQueryScratch.Clear();
    }

    private void OnTurnInContainerStartup(
        EntityUid uid,
        NcContractTurnInContainerComponent comp,
        ComponentStartup args
    ) =>
        ReindexTurnInContainer(uid, comp);

    private void OnTurnInContainerShutdown(
        EntityUid uid,
        NcContractTurnInContainerComponent comp,
        ComponentShutdown args
    ) =>
        RemoveTurnInContainerFromIndex(uid);

    public void ReindexTurnInContainer(EntityUid uid, NcContractTurnInContainerComponent? comp = null)
    {
        RemoveTurnInContainerFromIndex(uid);

        if (comp == null && !TryComp(uid, out comp))
            return;

        if (comp.Groups.Count == 0)
            return;

        var trackedGroups = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in comp.Groups)
        {
            if (string.IsNullOrWhiteSpace(group) || !trackedGroups.Add(group))
                continue;

            if (!_turnInContainersByGroup.TryGetValue(group, out var containers))
            {
                containers = new();
                _turnInContainersByGroup[group] = containers;
            }

            containers.Add(uid);
        }

        if (trackedGroups.Count > 0)
            _turnInContainerGroupsByEntity[uid] = trackedGroups;
    }

    private void RemoveTurnInContainerFromIndex(EntityUid uid)
    {
        if (!_turnInContainerGroupsByEntity.Remove(uid, out var groups))
            return;

        foreach (var group in groups)
        {
            if (!_turnInContainersByGroup.TryGetValue(group, out var containers))
                continue;

            containers.Remove(uid);
            if (containers.Count == 0)
                _turnInContainersByGroup.Remove(group);
        }
    }

    private void CollectTurnInContainersByGroup(string group, List<EntityUid> output)
    {
        output.Clear();

        if (string.IsNullOrWhiteSpace(group) ||
            !_turnInContainersByGroup.TryGetValue(group, out var containers))
            return;

        foreach (var container in containers)
            output.Add(container);

        for (var i = output.Count - 1; i >= 0; i--)
        {
            var container = output[i];
            if (container != EntityUid.Invalid &&
                Exists(container) &&
                TryComp(container, out NcContractTurnInContainerComponent? turnIn) &&
                turnIn.Groups.Contains(group))
                continue;

            RemoveTurnInContainerFromIndex(container);
            output.RemoveAt(i);
        }
    }
}
