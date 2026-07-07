namespace CS2MultiplayerMod.Core.Protocol.Messages
{
    /// <summary>
    /// Client → host: "stream me the current world now." Sent when a player runs the
    /// <c>/sync</c> command (chat or settings button) because they suspect their city has
    /// drifted. The host saves the live world and streams it to the requester — exactly
    /// the periodic drift-correcting resync, but on demand.
    /// </summary>
    public sealed class ResyncRequestMessage : INetMessage
    {
        public int OriginPlayerId;

        public ResyncRequestMessage() { }

        public ResyncRequestMessage(int originPlayerId)
        {
            OriginPlayerId = originPlayerId;
        }

        public MessageType Type => MessageType.ResyncRequest;

        public void Write(NetworkWriter writer) => writer.WriteInt(OriginPlayerId);

        public void Read(NetworkReader reader) => OriginPlayerId = reader.ReadInt();
    }
}
