using System;

namespace CS2MultiplayerMod.Core.Networking
{
    /// <summary>
    /// Opaque, value-type handle for a transport-level connection.
    ///
    /// On a host, every connected client has a unique id. On a client there is a
    /// single connection to the host, addressed by <see cref="Server"/>.
    /// </summary>
    public readonly struct ConnectionId : IEquatable<ConnectionId>
    {
        /// <summary>Sentinel meaning "no connection".</summary>
        public static readonly ConnectionId None = new ConnectionId(0);

        /// <summary>The host endpoint, from a client's point of view.</summary>
        public static readonly ConnectionId Server = new ConnectionId(1);

        public readonly int Value;

        public ConnectionId(int value)
        {
            Value = value;
        }

        public bool IsNone => Value == None.Value;

        public bool Equals(ConnectionId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is ConnectionId other && Equals(other);
        public override int GetHashCode() => Value;
        public override string ToString() => IsNone ? "Connection(None)" : "Connection(" + Value + ")";

        public static bool operator ==(ConnectionId a, ConnectionId b) => a.Value == b.Value;
        public static bool operator !=(ConnectionId a, ConnectionId b) => a.Value != b.Value;
    }
}
