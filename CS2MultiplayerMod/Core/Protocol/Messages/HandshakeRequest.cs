namespace CS2MultiplayerMod.Core.Protocol.Messages
{
    /// <summary>
    /// The client's answer to a <see cref="HandshakeChallenge"/>. The host validates
    /// <see cref="ProtocolVersion"/>, mod/game build compatibility, the DLC list and
    /// the password proof before anything else, so incompatible or unauthorized
    /// clients fail fast and clearly. <see cref="PasswordProof"/> is
    /// HMAC-SHA256(password, nonce ‖ channel-binding) — never the password itself.
    /// <see cref="DlcList"/> carries the sync-relevant DLC names the client owns
    /// (sorted, canonical): differing DLCs mean differing prefabs, which desync.
    /// </summary>
    public sealed class HandshakeRequest : INetMessage
    {
        public int ProtocolVersion;
        public string ModVersion;
        public string GameVersion;
        public string PlayerName;
        public byte[] PasswordProof;
        public string[] DlcList;

        public HandshakeRequest() { }

        public HandshakeRequest(int protocolVersion, string modVersion, string gameVersion,
                                string playerName, byte[] passwordProof, string[] dlcList = null)
        {
            ProtocolVersion = protocolVersion;
            ModVersion = modVersion;
            GameVersion = gameVersion;
            PlayerName = playerName;
            PasswordProof = passwordProof ?? System.Array.Empty<byte>();
            DlcList = dlcList ?? System.Array.Empty<string>();
        }

        public MessageType Type => MessageType.HandshakeRequest;

        public void Write(NetworkWriter writer)
        {
            writer.WriteInt(ProtocolVersion);
            writer.WriteString(ModVersion);
            writer.WriteString(GameVersion);
            writer.WriteString(PlayerName);
            writer.WriteInt(PasswordProof != null ? PasswordProof.Length : 0);
            if (PasswordProof != null && PasswordProof.Length > 0)
                writer.WriteBytes(PasswordProof, 0, PasswordProof.Length);

            int dlcCount = DlcList != null ? DlcList.Length : 0;
            if (dlcCount > ProtocolConstants.MaxDlcEntries) dlcCount = ProtocolConstants.MaxDlcEntries;
            writer.WriteInt(dlcCount);
            for (int i = 0; i < dlcCount; i++)
                writer.WriteString(DlcList[i] ?? string.Empty);
        }

        public void Read(NetworkReader reader)
        {
            ProtocolVersion = reader.ReadInt();
            ModVersion = reader.ReadString();
            GameVersion = reader.ReadString();
            PlayerName = reader.ReadString();
            int length = reader.ReadInt();
            if (length < 0 || length > 64)
                throw new ProtocolException("Implausible proof length: " + length + ".");
            PasswordProof = length > 0 ? reader.ReadBytes(length) : System.Array.Empty<byte>();

            int dlcCount = reader.ReadInt();
            if (dlcCount < 0 || dlcCount > ProtocolConstants.MaxDlcEntries)
                throw new ProtocolException("Implausible DLC count: " + dlcCount + ".");
            DlcList = dlcCount > 0 ? new string[dlcCount] : System.Array.Empty<string>();
            for (int i = 0; i < dlcCount; i++)
            {
                // DLC names end up in reject messages and logs, so they are sanitized
                // like any other display text instead of trusted off the wire.
                DlcList[i] = WireGuard.SanitizeText(reader.ReadString(), ProtocolConstants.MaxDlcNameLength);
            }
        }
    }
}
