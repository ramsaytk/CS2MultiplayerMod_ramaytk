namespace CS2MultiplayerMod.Core.Networking
{
    public enum TransportEventType
    {
        /// <summary>A new connection was established (host: a client joined; client: connected to host).</summary>
        Connected,

        /// <summary>A connection was closed, by either side or due to an error.</summary>
        Disconnected,

        /// <summary>A complete application payload arrived on a connection.</summary>
        Data,
    }

    /// <summary>
    /// A single transport occurrence, produced on background I/O threads and
    /// consumed on the game thread via <see cref="ITransport.Poll"/>.
    ///
    /// This indirection is the heart of the threading model: no game state is ever
    /// touched from an I/O thread. Events are queued and drained on the simulation
    /// thread where it is safe to mutate ECS data.
    /// </summary>
    public readonly struct TransportEvent
    {
        public readonly TransportEventType Type;
        public readonly ConnectionId Connection;

        /// <summary>For <see cref="TransportEventType.Data"/>: the payload bytes (owned by the consumer). Otherwise null.</summary>
        public readonly byte[] Payload;

        /// <summary>Optional human-readable reason, e.g. a disconnect cause. May be null.</summary>
        public readonly string Detail;

        private TransportEvent(TransportEventType type, ConnectionId connection, byte[] payload, string detail)
        {
            Type = type;
            Connection = connection;
            Payload = payload;
            Detail = detail;
        }

        public static TransportEvent Connected(ConnectionId connection) =>
            new TransportEvent(TransportEventType.Connected, connection, null, null);

        public static TransportEvent Disconnected(ConnectionId connection, string detail) =>
            new TransportEvent(TransportEventType.Disconnected, connection, null, detail);

        public static TransportEvent Data(ConnectionId connection, byte[] payload) =>
            new TransportEvent(TransportEventType.Data, connection, payload, null);
    }
}
