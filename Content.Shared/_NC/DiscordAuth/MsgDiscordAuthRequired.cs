using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._NC.DiscordAuth;

public sealed class MsgDiscordAuthRequired : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Command;
    public string Link = default!;
    public string ErrorMessage = default!;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        ErrorMessage = buffer.ReadString();
        Link = buffer.ReadString();
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write(ErrorMessage);
        buffer.Write(Link);
    }
}
