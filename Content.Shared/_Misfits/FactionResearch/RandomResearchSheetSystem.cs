using System.Collections.Generic;
using System.Text;
using Content.Shared._Misfits.FactionResearch.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._Misfits.FactionResearch;

public sealed class RandomResearchSheetSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;

    public const string ProtoPrefix = "N14RandomSheet";

    private readonly Dictionary<string, int> _counts = new();

    public override void Initialize()
    {
        base.Initialize();

        var ids = new List<string>();
        foreach (var config in _proto.EnumeratePrototypes<RandomResearchSheetPrototype>())
            ids.Add(config.ID);

        var sb = new StringBuilder();
        var changed = new Dictionary<Type, HashSet<string>>();

        foreach (var id in ids)
        {
            if (!_proto.TryIndex(id, out RandomResearchSheetPrototype? config))
                continue;

            for (var i = 0; i < config.Recipes.Count; i++)
            {
                sb.AppendLine("- type: entity");
                sb.AppendLine("  parent: N14RandomSheetBase");
                sb.AppendLine($"  id: {GetProto(id, i)}");
                sb.AppendLine("  components:");
                sb.AppendLine("  - type: Blueprint");
                sb.AppendLine("    recipes:");
                sb.AppendLine($"    - {config.Recipes[i].Id}");
            }

            _counts[id] = config.Recipes.Count;
        }

        if (sb.Length == 0)
            return;

        _proto.LoadString(sb.ToString(), true, changed);
        _proto.ReloadPrototypes(changed);
    }

    public int Count(string configId)
    {
        return _counts.GetValueOrDefault(configId, 0);
    }

    public string GetProto(string configId, int index)
    {
        return $"{ProtoPrefix}_{configId}_{index}";
    }
}
