namespace Content.Server.Magnits.QuestInstance;

public sealed partial class QuestInstancePresetPrototype
{
    public bool HasMapEntries => MapEntries.Count > 0;
}

public sealed partial class QuestInstanceMapEntry
{
    public float EffectiveWeight => MathF.Max(0f, Weight);
}
