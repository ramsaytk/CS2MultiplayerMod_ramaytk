namespace CS2MultiplayerMod.Core.Protocol.Messages
{
    /// <summary>
    /// Periodic keep-alive and latency probe. A ping carries the sender's monotonic
    /// clock in <see cref="SentAtMs"/>; the receiver answers with a heartbeat whose
    /// <see cref="EchoOfMs"/> returns that value, and the original sender measures
    /// round-trip as now − echo — both ends of the subtraction on ITS OWN clock, so
    /// the two machines' clocks never need to agree. An echo is never echoed back.
    /// </summary>
    public sealed class Heartbeat : INetMessage
    {
        /// <summary>Sender's monotonic clock (ms) when this heartbeat was sent.</summary>
        public long SentAtMs;

        /// <summary>0 for a ping; for an echo, the ping's <see cref="SentAtMs"/> being returned.</summary>
        public long EchoOfMs;

        public Heartbeat() { }

        public Heartbeat(long sentAtMs, long echoOfMs = 0)
        {
            SentAtMs = sentAtMs;
            EchoOfMs = echoOfMs;
        }

        public MessageType Type => MessageType.Heartbeat;

        public void Write(NetworkWriter writer)
        {
            writer.WriteLong(SentAtMs);
            writer.WriteLong(EchoOfMs);
        }

        public void Read(NetworkReader reader)
        {
            SentAtMs = reader.ReadLong();
            EchoOfMs = reader.ReadLong();
        }
    }
}
