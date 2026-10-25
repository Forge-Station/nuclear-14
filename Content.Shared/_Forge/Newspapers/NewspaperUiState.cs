using Robust.Shared.Serialization;
using Content.Shared._Forge.Paper;

namespace Content.Shared._Forge.Newspapers;

[Serializable, NetSerializable]
public enum NewspaperDeskUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public enum NewspaperCopyUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class NewspaperPublicationInfo
{
    public readonly int Id;
    public readonly string Name;
    public readonly int NextNumber;
    public NewspaperPublicationInfo(int id, string name, int nextNumber = 1) { Id = id; Name = name; NextNumber = nextNumber; }
}

[Serializable, NetSerializable]
public sealed class NewspaperDeskUiState : BoundUserInterfaceState
{
    public readonly NewspaperEdition Draft;
    // Archive headers only. Full layouts and their photo identities are requested separately.
    public readonly NewspaperEdition[] Editions;
    public readonly int PaperCount;
    public readonly NewspaperEdition[] Templates;
    public readonly NewspaperPublicationInfo[] Publications;
    public readonly Dictionary<int, string> PhotoKeys;
    public readonly int[] BufferedPhotos;
    public readonly long Revision;
    public readonly bool DraftPublished;

    public NewspaperDeskUiState(NewspaperEdition draft, NewspaperEdition[] editions, int paperCount, NewspaperEdition[] templates, NewspaperPublicationInfo[] publications, Dictionary<int, string>? photoKeys = null, int[]? bufferedPhotos = null, long revision = 0, bool draftPublished = false)
    {
        Draft = draft;
        Revision = revision;
        DraftPublished = draftPublished;
        Editions = editions;
        PaperCount = paperCount;
        Templates = templates;
        Publications = publications;
        PhotoKeys = photoKeys ?? new();
        BufferedPhotos = bufferedPhotos ?? Array.Empty<int>();
    }
}

[Serializable, NetSerializable]
public sealed class NewspaperCopyUiState : BoundUserInterfaceState
{
    public readonly NewspaperEdition Edition;

    public readonly PaperSurfaceAppearance? Surface;
    public readonly string PhotoKey;
    public readonly Dictionary<int, string> PhotoKeys;
    public NewspaperCopyUiState(NewspaperEdition edition, PaperSurfaceAppearance? surface = null, string photoKey = "", Dictionary<int, string>? photoKeys = null)
    {
        PhotoKeys = photoKeys ?? new();
        if (edition.PhotoId >= 0 && photoKey.Length > 0) PhotoKeys.TryAdd(edition.PhotoId, photoKey);
        Edition = edition;
        Surface = surface;
        PhotoKey = photoKey;
    }
}

[Serializable, NetSerializable]
public sealed class NewspaperSaveDraftMessage : BoundUserInterfaceMessage
{
    public readonly NewspaperEdition Draft;
    public readonly long Revision;
    public readonly int RequestId;

    public NewspaperSaveDraftMessage(NewspaperEdition draft, long revision = -1, int requestId = 0)
    {
        Draft = draft;
        Revision = revision; RequestId = requestId;
    }
}

[Serializable, NetSerializable]
public sealed class NewspaperPublishMessage : BoundUserInterfaceMessage
{
    public readonly NewspaperEdition Draft;
    public readonly long Revision;
    public readonly int RequestId;

    public NewspaperPublishMessage(NewspaperEdition draft, long revision = -1, int requestId = 0)
    {
        Draft = draft;
        Revision = revision; RequestId = requestId;
    }
}

[Serializable, NetSerializable]
public sealed class NewspaperPrintMessage : BoundUserInterfaceMessage
{
    public readonly int Count;
    public readonly int Edition;
    public readonly NewspaperPaper Paper;

    public NewspaperPrintMessage(int count, int edition, NewspaperPaper paper = NewspaperPaper.White)
    {
        Count = count;
        Edition = edition;
        Paper = paper;
    }
}

[Serializable, NetSerializable]
public sealed class NewspaperImageRequestMessage : BoundUserInterfaceMessage
{
    public readonly int Edition;
    public readonly int PhotoId;
    public NewspaperImageRequestMessage(int edition, int photoId = -1) { Edition = edition; PhotoId = photoId; }
}

[Serializable, NetSerializable]
public sealed class NewspaperImageMessage : BoundUserInterfaceMessage
{
    public readonly int PhotoId;
    public readonly byte[] Data;
    public readonly string PhotoKey;
    public NewspaperImageMessage(int photoId, byte[] data, string photoKey = "")
    {
        PhotoId = photoId;
        Data = data;
        PhotoKey = photoKey;
    }
}

[Serializable, NetSerializable]
public sealed class NewspaperSelectImageMessage : BoundUserInterfaceMessage
{
    public readonly int PhotoId;
    public NewspaperSelectImageMessage(int photoId) => PhotoId = photoId;
}

[Serializable, NetSerializable]
public sealed class NewspaperForgetImageMessage : BoundUserInterfaceMessage
{
    public readonly int PhotoId;
    public NewspaperForgetImageMessage(int photoId) => PhotoId = photoId;
}

[Serializable, NetSerializable]
public sealed class NewspaperSaveTemplateMessage : BoundUserInterfaceMessage
{
    public readonly NewspaperEdition Template;
    public NewspaperSaveTemplateMessage(NewspaperEdition template) => Template = template;
}

[Serializable, NetSerializable]
public sealed class NewspaperSwitchPublicationMessage : BoundUserInterfaceMessage
{
    public readonly NewspaperEdition Draft;
    public readonly long Revision;
    public readonly int RequestId;
    public readonly int PublicationId;
    // Zero ID creates a new publication with this name.
    public readonly string Name;
    public NewspaperSwitchPublicationMessage(NewspaperEdition draft, int publicationId, string name = "", long revision = -1, int requestId = 0)
    { Draft = draft; PublicationId = publicationId; Name = name; Revision = revision; RequestId = requestId; }
}

[Serializable, NetSerializable]
public sealed class NewspaperDraftResultMessage(int requestId, long revision, string error = "") : BoundUserInterfaceMessage
{
    public readonly int RequestId = requestId;
    public readonly long Revision = revision;
    public readonly string Error = error;
}

[Serializable, NetSerializable]
public sealed class NewspaperArchiveRequestMessage(int edition) : BoundUserInterfaceMessage
{
    public readonly int Edition = edition;
}

[Serializable, NetSerializable]
public sealed class NewspaperDeleteEditionMessage(int edition) : BoundUserInterfaceMessage
{
    public readonly int Edition = edition;
}

[Serializable, NetSerializable]
public sealed class NewspaperArchiveMessage(int index, NewspaperEdition edition, Dictionary<int, string> photoKeys) : BoundUserInterfaceMessage
{
    public readonly int Index = index;
    public readonly NewspaperEdition Edition = edition;
    public readonly Dictionary<int, string> PhotoKeys = photoKeys;
}
