using Lidgren.Network;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._NC.Sponsor;

/// <summary>
///     Sent by a client to request changing its sponsor cosmetic preferences
///     (custom OOC/LOOC name color and chosen ghost skin). The server validates
///     sponsorship and the required ghost-skin level before persisting.
/// </summary>
public sealed class MsgUpdateSponsorPreferences : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Command;

    public Color OOCColor;
    public Color LOOCColor;
    public string GhostSkin = string.Empty;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        OOCColor = new Color(buffer.ReadByte(), buffer.ReadByte(), buffer.ReadByte(), buffer.ReadByte());
        LOOCColor = new Color(buffer.ReadByte(), buffer.ReadByte(), buffer.ReadByte(), buffer.ReadByte());
        GhostSkin = buffer.ReadString();
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write(OOCColor.RByte);
        buffer.Write(OOCColor.GByte);
        buffer.Write(OOCColor.BByte);
        buffer.Write(OOCColor.AByte);
        buffer.Write(LOOCColor.RByte);
        buffer.Write(LOOCColor.GByte);
        buffer.Write(LOOCColor.BByte);
        buffer.Write(LOOCColor.AByte);
        buffer.Write(GhostSkin);
    }
}
