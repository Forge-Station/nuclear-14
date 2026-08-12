using System.Text;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Doors;
using Content.Shared.Doors.Systems;


namespace Content.Shared._Forge.Doors.UniqueKeyDoor;


public sealed class UniqueKeyDoorSystem : EntitySystem
{
    private const string PlaceholderRu =
        "\u0443\u043A\u0430\u0436\u0438 \u0438\u043C\u044F \u043A\u043E\u043C\u043D\u0430\u0442\u044B";

    private const string PlaceholderEn = "set room name";
    private const string PlaceholderTemplate = "template";

    [Dependency] private readonly AccessReaderSystem _accessReaderSystem = default!;
    [Dependency] private readonly SharedDoorSystem _doorSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<UniqueKeyDoorComponent, MapInitEvent>(OnDoorMapInit);
        SubscribeLocalEvent<UniqueDoorKeyComponent, MapInitEvent>(OnKeyMapInit);
        SubscribeLocalEvent<UniqueKeyDoorComponent, BeforeDoorOpenedEvent>(OnBeforeDoorOpened);
        SubscribeLocalEvent<UniqueKeyDoorComponent, BeforeDoorClosedEvent>(OnBeforeDoorClosed);
    }

    private void OnDoorMapInit(Entity<UniqueKeyDoorComponent> ent, ref MapInitEvent args)
    {
        RefreshDoorBindingFromMetadata(ent.Owner, ent.Comp);
    }

    private void OnKeyMapInit(Entity<UniqueDoorKeyComponent> ent, ref MapInitEvent args)
    {
        RefreshKeyBindingFromMetadata(ent.Owner, ent.Comp);
    }

    private void OnBeforeDoorOpened(Entity<UniqueKeyDoorComponent> ent, ref BeforeDoorOpenedEvent args)
    {
        if (args.Cancelled || args.User == null || _doorSystem.AccessType != SharedDoorSystem.AccessTypes.Id)
            return;

        if (!TryComp<AccessReaderComponent>(ent, out var access))
            return;

        if (HasUniqueAccess(ent.Owner, args.User.Value, access, ent.Comp))
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

        if (HasUniqueAccess(ent.Owner, args.User.Value, access, ent.Comp))
            return;

        args.Cancel();
    }

    private bool HasUniqueAccess(
        EntityUid doorUid,
        EntityUid userUid,
        AccessReaderComponent accessReader,
        UniqueKeyDoorComponent uniqueKeyDoor
    )
    {
        if (!accessReader.Enabled)
            return true;

        var accessSources = _accessReaderSystem.FindPotentialAccessItems(userUid);
        var accessTags = _accessReaderSystem.FindAccessTags(userUid, accessSources);
        foreach (var accessTag in accessTags)
            if (uniqueKeyDoor.MasterKeyTags.Contains(accessTag))
                return true;

        if (!TryResolveDoorKeyId(doorUid, uniqueKeyDoor, out var doorKeyId))
            return false;

        foreach (var source in accessSources)
        {
            if (!TryComp<UniqueDoorKeyComponent>(source, out var key))
                continue;

            if (!TryResolveKeyId(source, key, out var keyId))
                continue;

            if (string.Equals(keyId, doorKeyId, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private bool TryResolveDoorKeyId(EntityUid doorUid, UniqueKeyDoorComponent door, out string keyId)
    {
        RefreshDoorBindingFromMetadata(doorUid, door);

        var current = door.DoorKeyId?.Trim();
        if (string.IsNullOrWhiteSpace(current))
        {
            keyId = string.Empty;
            return false;
        }

        keyId = current;
        return true;
    }

    private bool TryResolveKeyId(EntityUid keyUid, UniqueDoorKeyComponent key, out string keyId)
    {
        RefreshKeyBindingFromMetadata(keyUid, key);

        var current = key.KeyId?.Trim();
        if (string.IsNullOrWhiteSpace(current))
        {
            keyId = string.Empty;
            return false;
        }

        keyId = current;
        return true;
    }

    private bool RefreshDoorBindingFromMetadata(EntityUid uid, UniqueKeyDoorComponent component)
    {
        if (!TryGetMetadataBinding(uid, out var bindId))
        {
            if (string.IsNullOrWhiteSpace(component.DoorKeyId))
                return false;

            component.DoorKeyId = null;
            Dirty(uid, component);
            return false;
        }

        if (string.Equals(component.DoorKeyId, bindId, StringComparison.OrdinalIgnoreCase))
            return true;

        component.DoorKeyId = bindId;
        Dirty(uid, component);
        return true;
    }

    private bool RefreshKeyBindingFromMetadata(EntityUid uid, UniqueDoorKeyComponent component)
    {
        if (!TryGetMetadataBinding(uid, out var bindId))
        {
            if (string.IsNullOrWhiteSpace(component.KeyId))
                return false;

            component.KeyId = null;
            Dirty(uid, component);
            return false;
        }

        if (string.Equals(component.KeyId, bindId, StringComparison.OrdinalIgnoreCase))
            return true;

        component.KeyId = bindId;
        Dirty(uid, component);
        return true;
    }

    private bool TryGetMetadataBinding(EntityUid uid, out string bindId)
    {
        bindId = string.Empty;

        if (!TryComp(uid, out MetaDataComponent? meta))
            return false;

        if (TryNormalizeQuotedBinding(meta.EntityDescription, out var normalized))
        {
            bindId = normalized;
            return true;
        }

        if (TryNormalizeQuotedBinding(meta.EntityName, out normalized))
        {
            bindId = normalized;
            return true;
        }

        return false;
    }

    private bool TryNormalizeQuotedBinding(string text, out string bindId)
    {
        bindId = string.Empty;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        if (!TryExtractQuotedContent(text.Trim(), out var source))
            return false;

        if (IsPlaceholderQuotedValue(source))
            return false;

        var normalized = NormalizeBindingId(source);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        bindId = normalized;
        return true;
    }

    private static bool TryExtractQuotedContent(string text, out string content)
    {
        content = string.Empty;

        var left = text.IndexOf('"');
        if (left < 0)
            return false;

        var right = text.IndexOf('"', left + 1);
        if (right <= left + 1)
            return false;

        content = text[(left + 1)..right].Trim();
        return !string.IsNullOrWhiteSpace(content);
    }

    private static string NormalizeBindingId(string value)
    {
        var text = value.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var builder = new StringBuilder(text.Length);
        var pendingSeparator = false;

        foreach (var chr in text)
            if (char.IsLetterOrDigit(chr))
            {
                if (pendingSeparator && builder.Length > 0)
                    builder.Append('_');

                builder.Append(char.ToLowerInvariant(chr));
                pendingSeparator = false;
            }
            else if (builder.Length > 0)
                pendingSeparator = true;

        return builder.ToString().Trim('_');
    }

    private static bool IsPlaceholderQuotedValue(string value)
    {
        var lowered = value.Trim().ToLowerInvariant();
        return lowered == PlaceholderRu || lowered == PlaceholderEn || lowered == PlaceholderTemplate;
    }
}