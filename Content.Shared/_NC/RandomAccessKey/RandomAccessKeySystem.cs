using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Construction;
using Content.Shared.Doors.Components;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Shared._NC.RandomAccessKey;

public sealed class RandomAccessKeySystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly MetaDataSystem _meta = default!;
    private const string RandomAccessPrefix = "RandomAccess";
    private const string Key = "N14IDKeyIronEmpty";
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RandomAccessKeyComponent, ConstructionCompletedEvent>(OmConstructionCompleted);
    }

    private void OmConstructionCompleted(Entity<RandomAccessKeyComponent> ent,
        ref ConstructionCompletedEvent args)
    {
        if (args.UserUid == null)
            return;

        if (!TryComp(ent.Owner, out DoorComponent? door))
            return;

        var randomKey = _random.Next(1000, 9999);
        var prototypeId = $"{RandomAccessPrefix}{randomKey}";
        var prototype = new AccessLevelPrototype
        {
            ID = prototypeId,
            Name = $"Ключ #{randomKey}"
        };
        var accessReader = EnsureComp<AccessReaderComponent>(ent.Owner);

        accessReader.AccessLists.Add(new HashSet<ProtoId<AccessLevelPrototype>> { prototype.ID });

        var userCord = _transform.GetMapCoordinates(args.UserUid.Value);
        var doorKey = Spawn(Key, userCord);
        var accessKey = EnsureComp<AccessComponent>(doorKey);

        _meta.SetEntityName(doorKey, $"Ключ #{randomKey}");
        accessKey.Tags.Clear();
        accessKey.Tags.Add(prototype.ID);
        Dirty(doorKey, accessKey);
        _hands.PickupOrDrop(args.UserUid, doorKey);
        door.CanPry = false;
        door.BumpOpen = false;
        Dirty(ent.Owner, door);
    }
}
