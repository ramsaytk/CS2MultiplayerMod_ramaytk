namespace CS2MultiplayerMod.Core.Protocol
{
    /// <summary>
    /// Discriminator written as the first byte of every payload. Values are explicit
    /// and must remain stable across versions (append new ones, never renumber).
    /// </summary>
    public enum MessageType : byte
    {
        Unknown = 0,

        /// <summary>Client → host: introduce protocol version and identity.</summary>
        HandshakeRequest = 1,

        /// <summary>Host → client: accept or reject, assigning a player id on success.</summary>
        HandshakeResponse = 2,

        /// <summary>Either direction: liveness keep-alive.</summary>
        Heartbeat = 3,

        /// <summary>Either direction: free-text chat / system notice.</summary>
        Chat = 4,

        /// <summary>Either direction: an envelope carrying a serialized simulation command.</summary>
        SimulationCommand = 5,

        /// <summary>Host → clients: a replicated slice of authoritative state (money, population, …).</summary>
        StateSnapshot = 6,

        /// <summary>Either direction: a player's camera/cursor position, relayed by the host.</summary>
        PlayerState = 7,

        /// <summary>Host → clients: one chunk of a large named byte stream (e.g. a savegame).</summary>
        BlobChunk = 8,

        /// <summary>
        /// Client → host: a player edited a shared, player-editable setting (taxes,
        /// policies, …). The host applies it and re-broadcasts via <see cref="StateSnapshot"/>.
        /// </summary>
        StateEdit = 9,

        /// <summary>
        /// Client → host: the player ran /sync and wants the current world streamed to
        /// them immediately (on-demand drift correction).
        /// </summary>
        ResyncRequest = 10,

        /// <summary>
        /// Host → client, sent immediately on connect: a random nonce the client must
        /// fold into its password proof (challenge-response — the password itself never
        /// travels). Also carries the host's protocol version so an incompatible client
        /// can fail fast with a clear message.
        /// </summary>
        HandshakeChallenge = 11,
    }
}
