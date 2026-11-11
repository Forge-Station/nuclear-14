using Content.Shared._Forge.Newspapers;
using Content.Shared.Materials;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.Newspapers;

[RegisterComponent]
public sealed partial class NewspaperDeskComponent : Component
{
    [DataField] public EntProtoId PrintedPrototype = "N14PrintedBulletin";
    [DataField] public ProtoId<MaterialPrototype> PaperMaterial = "Paper";
    [DataField] public int PaperPerCopy = 100;
    [DataField] public TimeSpan PrintCooldown = TimeSpan.FromSeconds(2);
    [DataField] public TimeSpan PublishCooldown = TimeSpan.FromSeconds(2);
    [DataField] public NewspaperEdition Draft = new();
    [DataField] public Dictionary<int, NewspaperEdition> PublicationDrafts = new();
    [DataField] public int NextPublicationId;
    public long Revision;
    public TimeSpan NextPublish;
    public TimeSpan NextPrint;
    [DataField] public List<NewspaperEdition> Editions = new();
    [DataField] public int NextEditionId;
    [DataField] public Dictionary<int, int> LastEditionNumbers = new();
    [DataField] public Dictionary<int, byte[]> Photos = new();
    [DataField] public List<int> BufferedPhotos = new();
    [DataField] public int NextPhotoId;
    public Dictionary<int, string> PhotoKeys = new();
    [DataField] public List<NewspaperEdition> Templates = new();
}
