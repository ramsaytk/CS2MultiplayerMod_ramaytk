using System.Collections.Concurrent;

namespace CS2MultiplayerMod.Game.Sync.Infrastructure
{
    /// <summary>
    /// Bounded enqueue for the sync systems' incoming-message queues. The queues fill
    /// while gameplay sync is gated (e.g. during a map load) or when a peer floods;
    /// shedding the oldest beyond a cap keeps memory bounded, and the periodic world
    /// resync repairs whatever the shed messages would have applied.
    /// </summary>
    internal static class SyncInbox
    {
        public const int DefaultCap = 1024;

        public static void Push<T>(ConcurrentQueue<T> queue, T item, int cap = DefaultCap)
        {
            queue.Enqueue(item);
            T dropped;
            while (queue.Count > cap && queue.TryDequeue(out dropped)) { }
        }
    }
}
