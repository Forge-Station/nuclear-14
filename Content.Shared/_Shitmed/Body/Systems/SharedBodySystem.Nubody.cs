using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Shared.Body;
using Content.Shared.Body.Part;
using Content.Shared.Body.Organ;
using Robust.Shared.Containers;

namespace Content.Shared.Body.Systems;

/// <summary>
/// Выражает API тела из Shitmed через устройство Nubody.
/// </summary>
/// <remarks>
/// У Shitmed тело было деревом слотов: часть владела слотами, в слоты вставлялись
/// другие части и органы, у каждого слота было имя. Nubody (апстрим, #42419) держит
/// все органы тела в одном контейнере <see cref="BodyComponent.ContainerID"/>, а
/// иерархию - в <see cref="ParentOrganComponent"/> и <see cref="ChildOrganComponent"/>.
/// Слотов нет вообще, поэтому их имена здесь остались только как ключи для прототипов
/// и условий хирургии: они больше ничего не адресуют.
/// </remarks>
public partial class SharedBodySystem
{
    [Dependency] private readonly SharedContainerSystem _bodyContainer = default!;

    /// <summary>
    /// Имя слота, в который встала бы такая часть. Осталось ключом для прототипов.
    /// </summary>
    public string GetSlotFromBodyPart(BodyPartComponent part)
    {
        return part.Symmetry == BodyPartSymmetry.None
            ? part.PartType.ToString().ToLowerInvariant()
            : $"{part.Symmetry.ToString().ToLowerInvariant()} {part.PartType.ToString().ToLowerInvariant()}";
    }

    /// <summary>
    /// Имя контейнера органа по имени слота. Nubody держит органы в одном контейнере
    /// тела, так что отдельных контейнеров под слоты больше нет.
    /// </summary>
    public static string GetOrganContainerId(string slotId)
    {
        return BodyComponent.ContainerID;
    }

    /// <summary>
    /// Все части тела: органы тела, у которых есть <see cref="BodyPartComponent"/>.
    /// </summary>
    public IEnumerable<(EntityUid Id, BodyPartComponent Component)> GetBodyChildren(
        EntityUid? bodyId,
        BodyComponent? body = null)
    {
        if (bodyId is not { } id)
            yield break;

        foreach (var organ in Body.EnumerateOrgans<BodyPartComponent>(id))
        {
            yield return (organ.Owner, organ.Comp2);
        }
    }

    /// <inheritdoc cref="GetBodyChildren"/>
    public IEnumerable<(EntityUid Id, BodyPartComponent Component)> GetBodyChildrenOfType(
        EntityUid? bodyId,
        BodyPartType type,
        BodyComponent? body = null,
        BodyPartSymmetry? symmetry = null)
    {
        return GetBodyChildren(bodyId, body)
            .Where(part => part.Component.PartType == type
                           && (symmetry == null || part.Component.Symmetry == symmetry));
    }

    /// <summary>
    /// Органы части тела: её дети, которые сами частями не являются.
    /// </summary>
    public IEnumerable<(EntityUid Id, ShitmedOrganComponent Component)> GetPartOrgans(
        EntityUid? partId,
        BodyPartComponent? part = null)
    {
        if (partId is not { } id)
            yield break;

        foreach (var child in Relations.AllChildren(id))
        {
            if (HasComp<BodyPartComponent>(child)
                || !TryComp<ShitmedOrganComponent>(child, out var organ))
                continue;

            yield return (child.Owner, organ);
        }
    }

    /// <summary>
    /// Органы части тела, у которых есть компонент указанного типа.
    /// </summary>
    public bool TryGetBodyPartOrgans(
        EntityUid? partId,
        Type componentType,
        out List<(EntityUid Id, ShitmedOrganComponent Organ)> organs)
    {
        organs = GetPartOrgans(partId)
            .Where(organ => HasComp(organ.Id, componentType))
            .Select(organ => (organ.Id, Organ: organ.Component))
            .ToList();

        return organs.Count > 0;
    }

    /// <summary>
    /// Часть, к которой присоединена эта, и имя слота, в котором она была бы.
    /// </summary>
    public (EntityUid Parent, string Slot)? GetParentPartAndSlotOrNull(EntityUid id)
    {
        if (!TryComp<ChildOrganComponent>(id, out var child)
            || child.Parent is not { } parent
            || !TryComp<BodyPartComponent>(id, out var part))
            return null;

        return (parent, GetSlotFromBodyPart(part));
    }

    /// <summary>
    /// Слотов у Nubody нет, создавать нечего: имя слота отдаётся как есть.
    /// </summary>
    public bool TryCreatePartSlot(
        EntityUid? parentId,
        string slotId,
        BodyPartType partType,
        out string? slot,
        BodyPartComponent? parent = null)
    {
        slot = parentId == null ? null : slotId;
        return slot != null;
    }

    /// <summary>
    /// Можно ли присоединить к части ещё одну в этот слот: у Nubody ограничение
    /// только одно - такой части ещё нет среди детей.
    /// </summary>
    public bool CanAttachToSlot(EntityUid? partId, string slotId)
    {
        if (partId is not { } id)
            return false;

        foreach (var child in Relations.AllChildren(id))
        {
            if (TryComp<BodyPartComponent>(child, out var part) && GetSlotFromBodyPart(part) == slotId)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Присоединяет часть тела к другой: орган уезжает в контейнер тела, а связь
    /// родитель-ребёнок ставится напрямую.
    /// </summary>
    public bool AttachPart(
        EntityUid? parentPartId,
        string slotId,
        EntityUid partId,
        BodyPartComponent? parentPart = null,
        BodyPartComponent? part = null)
    {
        if (parentPartId is not { } parentId
            || !Resolve(parentId, ref parentPart, false)
            || !Resolve(partId, ref part, false)
            || parentPart.Body is not { } body)
            return false;

        if (!InsertIntoBody(body, partId))
            return false;

        Relations.Relate(parentId, partId);
        SyncPartBody((partId, part), body);
        return true;
    }

    /// <summary>
    /// Вставляет орган в часть тела.
    /// </summary>
    public bool InsertOrgan(
        EntityUid? partId,
        EntityUid organId,
        string slotId,
        BodyPartComponent? part = null,
        ShitmedOrganComponent? organ = null)
    {
        if (partId is not { } id
            || !Resolve(id, ref part, false)
            || !Resolve(organId, ref organ, false)
            || part.Body is not { } body)
            return false;

        if (!InsertIntoBody(body, organId))
            return false;

        Relations.Relate(id, organId);

        organ.Body = body;
        organ.SlotId = slotId;
        organ.OriginalBody ??= body;
        Dirty(organId, organ);
        return true;
    }

    /// <summary>
    /// Вынимает орган из части тела.
    /// </summary>
    public bool RemoveOrgan(EntityUid organId, ShitmedOrganComponent organ)
    {
        Relations.Orphan(organId);

        if (!_bodyContainer.TryGetContainingContainer((organId, null, null), out var container)
            || !_bodyContainer.Remove(organId, container))
            return false;

        organ.Body = null;
        organ.SlotId = string.Empty;
        Dirty(organId, organ);
        return true;
    }

    /// <summary>
    /// Отмечает орган как уже использованный в этом теле - чтобы повторная пересадка
    /// того же органа не лечила снова.
    /// </summary>
    public bool TrySetOrganUsed(EntityUid organId, bool used, ShitmedOrganComponent? organ = null)
    {
        if (!Resolve(organId, ref organ, false) || organ.Used == used)
            return false;

        organ.Used = used;
        Dirty(organId, organ);
        return true;
    }

    /// <summary>
    /// Отрывает часть тела от тела вместе со всем, что на ней держится.
    /// </summary>
    public void DropPart(Entity<BodyPartComponent?> part)
    {
        if (!Resolve(part, ref part.Comp, false))
            return;

        // Nubody отрывает конечности через DetachableOrgan: он сам создаёт
        // "оторванное тело" и перекладывает туда орган со всеми детьми.
        if (HasComp<DetachableOrganComponent>(part))
        {
            Detachable.Detach(part.Owner);
            SyncPartBody((part.Owner, part.Comp), null);
            return;
        }

        Relations.Orphan(part.Owner);

        if (_bodyContainer.TryGetContainingContainer((part.Owner, null, null), out var container))
            _bodyContainer.Remove(part.Owner, container);

        SyncPartBody((part.Owner, part.Comp), null);
    }

    /// <summary>
    /// Отрывает часть тела и рассыпает её содержимое. Вызывается порогом разрушения.
    /// </summary>
    public void GibPart(EntityUid partId, BodyPartComponent? part = null)
    {
        if (!Resolve(partId, ref part, false))
            return;

        DropSlotContents((partId, part));
        DropPart((partId, part));
    }

    /// <summary>
    /// Отрывает всё, что держится на этой части.
    /// </summary>
    public void DropSlotContents(Entity<BodyPartComponent> part)
    {
        foreach (var child in Relations.AllChildren(part.Owner).ToList())
        {
            if (TryComp<BodyPartComponent>(child, out var childPart))
            {
                DropPart((child.Owner, childPart));
                continue;
            }

            if (TryComp<ShitmedOrganComponent>(child, out var organ))
                RemoveOrgan(child.Owner, organ);
        }
    }

    private bool InsertIntoBody(EntityUid body, EntityUid organId)
    {
        if (!_bodyContainer.TryGetContainer(body, BodyComponent.ContainerID, out var container))
        {
            Log.Error($"{ToPrettyString(body)} is missing its organ container ({BodyComponent.ContainerID}).");
            return false;
        }

        return _bodyContainer.Insert(organId, container, force: true);
    }

    /// <summary>
    /// Органы тела, у которых есть компонент <typeparamref name="T"/>, вместе с их
    /// органными компонентами Shitmed.
    /// </summary>
    public bool TryGetBodyOrganComponents<T>(
        EntityUid uid,
        [NotNullWhen(true)] out List<(T Comp, ShitmedOrganComponent Organ)>? comps,
        BodyComponent? body = null)
        where T : IComponent
    {
        comps = null;
        if (!Resolve(uid, ref body, false))
            return false;

        var found = new List<(T Comp, ShitmedOrganComponent Organ)>();
        foreach (var organ in Body.EnumerateOrgans<T>((uid, body)))
        {
            if (TryComp<ShitmedOrganComponent>(organ.Owner, out var shitmed))
                found.Add((organ.Comp2, shitmed));
        }

        if (found.Count == 0)
            return false;

        comps = found;
        return true;
    }
}
