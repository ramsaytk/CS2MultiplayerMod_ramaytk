namespace CS2MultiplayerMod.Core.Protocol.Messages
{
    /// <summary>
    /// A client's edit of a player-editable state channel (taxes, policies, fees, …).
    /// The body uses the exact same encoding as the channel's snapshot, so the host can
    /// apply it with the channel's regular <c>Apply</c> and the next
    /// <see cref="StateSnapshotMessage"/> broadcast confirms it to everyone — the host
    /// stays the single arbiter while every player gets to edit.
    /// </summary>
    public sealed class StateEditMessage : INetMessage
    {
        public int OriginPlayerId;
        public byte ChannelId;
        public byte[] Data;

        public StateEditMessage() { }

        public StateEditMessage(int originPlayerId, byte channelId, byte[] data)
        {
            OriginPlayerId = originPlayerId;
            ChannelId = channelId;
            Data = data ?? System.Array.Empty<byte>();
        }

        public MessageType Type => MessageType.StateEdit;

        public void Write(NetworkWriter writer)
        {
            writer.WriteInt(OriginPlayerId);
            writer.WriteByte(ChannelId);
            writer.WriteInt(Data != null ? Data.Length : 0);
            if (Data != null && Data.Length > 0)
                writer.WriteBytes(Data, 0, Data.Length);
        }

        public void Read(NetworkReader reader)
        {
            OriginPlayerId = reader.ReadInt();
            ChannelId = reader.ReadByte();
            int length = reader.ReadInt();
            Data = length > 0 ? reader.ReadBytes(length) : System.Array.Empty<byte>();
        }
    }
}
