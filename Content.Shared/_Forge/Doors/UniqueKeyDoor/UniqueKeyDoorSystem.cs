using System;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Doors;
using Content.Shared.Doors.Systems;
using Content.Shared.Interaction;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Doors.UniqueKeyDoor;

/// <summary>
/// Handles explicit key-tag access for doors with <see cref="UniqueKeyDoorComponent"/>.
/// </summary>
public sealed class UniqueKeyDoorSystem : EntitySystem
{
    [Dependency] private readonly AccessReaderSystem _accessReaderSystem = default!;
    [Dependency] private readonly SharedDoorSystem _doorSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<UniqueKeyDoorComponent, BeforeDoorOpenedEvent>(OnBeforeDoorOpened);
        SubscribeLocalEvent<UniqueKeyDoorComponent, BeforeDoorClosedEvent>(OnBeforeDoorClosed);
        SubscribeLocalEvent<UniqueKeyDoorComponent, InteractUsingEvent>(OnInteractUsing);
    }

    private void OnBeforeDoorOpened(Entity<UniqueKeyDoorComponent> ent, ref BeforeDoorOpenedEvent args)
    {
        if (args.Cancelled || args.User == null || _doorSystem.AccessType != SharedDoorSystem.AccessTypes.Id)
            return;

        if (!TryComp<AccessReaderComponent>(ent, out var access))
            return;

        if (HasUniqueAccess(args.User.Value, access, ent.Comp))
        {
            _accessReaderSystem.LogAccess((ent.Owner, access), args.User.Value);
            return;
        }

        args.Cancel();
    }

    private void OnBeforeDoorClosed(Entity<UniqueKeyDoorComponent> ent, ref BeforeDoorClosedEvent args)
    {
        if (args.Cancelled || args.User == null || _doorSystem.AccessType != SharedDoorSystem.AccessTypes.Id)
            return;

        if (!TryComp<AccessReaderComponent>(ent, out var access))
            return;

        if (HasUniqueAccess(args.User.Value, access, ent.Comp))
            return;

        args.Cancel();
    }

    private void OnInteractUsing(Entity<UniqueKeyDoorComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp<UniqueDoorKeyComponent>(args.Used, out var key))
            return;

        // Only blank keys can be linked to a door.
        if (!string.IsNullOrWhiteSpace(key.KeyId))
            return;

        if (ent.Comp.LinkedKeyCount >= ent.Comp.MaxLinkedKeys)
            return;

        if (string.IsNullOrWhiteSpace(ent.Comp.DoorKeyId))
            ent.Comp.DoorKeyId = $"door_{Guid.NewGuid():N}";

        key.KeyId = ent.Comp.DoorKeyId;
        ent.Comp.LinkedKeyCount++;
        args.Handled = true;

        Dirty(args.Used, key);
        Dirty(ent.Owner, ent.Comp);
    }

    private bool HasUniqueAccess(EntityUid userUid, AccessReaderComponent accessReader, UniqueKeyDoorComponent uniqueKeyDoor)
    {
        if (!accessReader.Enabled)
            return true;

        var accessSources = _accessReaderSystem.FindPotentialAccessItems(userUid);
        var accessTags = _accessReaderSystem.FindAccessTags(userUid, accessSources);
        var accessTagSet = new HashSet<ProtoId<AccessLevelPrototype>>(accessTags);

        if (uniqueKeyDoor.MasterKeyTags.Overlaps(accessTagSet))
            return true;

        var doorKeyId = uniqueKeyDoor.DoorKeyId?.Trim();
        if (string.IsNullOrWhiteSpace(doorKeyId))
            return false;

        foreach (var source in accessSources)
        {
            if (!TryComp<UniqueDoorKeyComponent>(source, out var key))
                continue;

            if (string.IsNullOrWhiteSpace(key.KeyId))
                continue;

            if (string.Equals(key.KeyId, doorKeyId, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
