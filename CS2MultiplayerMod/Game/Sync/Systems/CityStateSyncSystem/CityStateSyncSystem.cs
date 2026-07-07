using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using Game;
using Unity.Entities;
using CS2MultiplayerMod.Core.Protocol;
using CS2MultiplayerMod.Core.Protocol.Messages;
using CS2MultiplayerMod.Core.Session;

using CS2MultiplayerMod.Game.Sync.Infrastructure;
using CS2MultiplayerMod.Game.Sync.Channels;
namespace CS2MultiplayerMod.Game.Sync.Systems
{
    /// <summary>
    /// Drives city-state replication each simulation tick. On the host it periodically
    /// captures every <see cref="IStateChannel"/> and broadcasts a snapshot; on a client
    /// it applies snapshots the session hands it. Channels are routed by id, so adding a
    /// new synced value is a one-line registration in <see cref="OnCreate"/>.
    ///
    /// Channels come in two flavors:
    ///   authoritative — values the simulation owns (money, population, XP, tourism,
    ///       statistics): one source of truth, host → clients only.
    ///   editable — settings any player may change (taxes, policies, service fees,
    ///       simulation speed): a client detects its local edit (current capture differs
    ///       from what the host last sent), ships it to the host as a
    ///       <see cref="StateEditMessage"/> (same encoding as a snapshot), the host
    ///       applies it, and the next snapshot broadcast carries it to everyone. While
    ///       an edit is in flight the client skips applying stale snapshots for that
    ///       channel so its change doesn't flicker back; the host stays the arbiter
    ///       (last writer wins) so players can never diverge for more than a snapshot.
    ///
    /// Incoming snapshots/edits are delivered (on the simulation thread) by an inner
    /// observer and queued, then applied here in <see cref="OnUpdate"/> where this
    /// system's <see cref="EntityManager"/> is valid.
    /// </summary>
    public partial class CityStateSyncSystem : GameSystemBase
    {
        /// <summary>How often the host publishes a fresh snapshot.</summary>
        private const long SnapshotIntervalMs = 1000;

        /// <summary>How often a client compares its local editable state against the host's.</summary>
        private const long EditDetectIntervalMs = 250;

        /// <summary>How long a client trusts its own in-flight edit over incoming snapshots.</summary>
        private const long EditPendingTimeoutMs = 5000;

        private readonly Dictionary<byte, IStateChannel> _channels = new Dictionary<byte, IStateChannel>();
        private readonly HashSet<byte> _editable = new HashSet<byte>();
        private readonly ConcurrentQueue<StateSnapshotMessage> _incoming = new ConcurrentQueue<StateSnapshotMessage>();
        private readonly ConcurrentQueue<StateEditMessage> _incomingEdits = new ConcurrentQueue<StateEditMessage>();
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        // Client-side edit tracking: what the host last sent per editable channel, and
        // the edit we shipped and are waiting to see confirmed in a snapshot.
        private readonly Dictionary<byte, byte[]> _lastHostPayload = new Dictionary<byte, byte[]>();
        private readonly Dictionary<byte, PendingEdit> _pendingEdits = new Dictionary<byte, PendingEdit>();

        private Observer _observer;
        private long _lastSnapshotMs;
        private long _lastEditScanMs;
        private long _lastLogMs;
        private int _applied;

        private struct PendingEdit
        {
            public byte[] Payload;
            public long SentMs;
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            // Simulation-owned values: one source of truth, host → clients.
            Register(new MoneyStateChannel());
            Register(new PopulationStateChannel());
            Register(new XpStateChannel());
            Register(new MilestoneStateChannel());
            Register(new DevTreePointsStateChannel());
            Register(new TourismStateChannel());
            Register(new StatisticsStateChannel());
            Register(new WeatherStateChannel());
            Register(new GameClockStateChannel());

            // Player-editable settings: every player may change them; the host arbitrates.
            RegisterEditable(new TaxStateChannel());
            RegisterEditable(new CityPolicyStateChannel());
            RegisterEditable(new ServiceFeeStateChannel());
            RegisterEditable(new ServiceBudgetStateChannel());
            RegisterEditable(new SimulationSpeedStateChannel());
            RegisterEditable(new LoanStateChannel());

            Mod.log.Info(nameof(CityStateSyncSystem) + " ready with " + _channels.Count +
                         " state channel(s), " + _editable.Count + " player-editable.");

            if (Mod.Service != null)
            {
                _observer = new Observer(_incoming, _incomingEdits);
                Mod.Service.Session.AddObserver(_observer);
            }
        }

        protected override void OnDestroy()
        {
            if (_observer != null && Mod.Service != null)
                Mod.Service.Session.RemoveObserver(_observer);
            base.OnDestroy();
        }

        private void Register(IStateChannel channel) => _channels[channel.ChannelId] = channel;

        private void RegisterEditable(IStateChannel channel)
        {
            Register(channel);
            _editable.Add(channel.ChannelId);
        }

        protected override void OnUpdate()
        {
            MultiplayerService service = Mod.Service;
            if (service == null) return;

            MultiplayerSession session = service.Session;
            if (!service.GameplaySyncReady)
            {
                // Leaving a session invalidates everything we knew about the host's state.
                if (_lastHostPayload.Count > 0) { _lastHostPayload.Clear(); _pendingEdits.Clear(); }
                return;
            }

            if (session.Role == SessionRole.Host)
            {
                ApplyIncomingEdits();
                CaptureAndBroadcast(session);
            }
            else
            {
                DetectLocalEdits(session);
                ApplyIncoming();
            }
        }

        // ---- Host ------------------------------------------------------------


        private void CaptureAndBroadcast(MultiplayerSession session)
        {
            long now = _clock.ElapsedMilliseconds;
            if (_lastSnapshotMs != 0 && now - _lastSnapshotMs < SnapshotIntervalMs) return;
            _lastSnapshotMs = now;

            int sent = 0;
            foreach (var pair in _channels)
            {
                var writer = new NetworkWriter(64);
                if (pair.Value.Capture(EntityManager, writer)) { session.SendState(pair.Key, writer.ToArray()); sent++; }
            }

            // Heartbeat every ~30 s so the log shows state replication is alive without spam.
            if (now - _lastLogMs >= 30000) { _lastLogMs = now; Mod.Verbose("[MP] CityState: broadcasting " + sent + " channel(s)/snapshot to clients."); }
        }

        // ---- Client ------------------------------------------------------------

        /// <summary>
        /// A local edit shows up as the channel capturing something different from what
        /// the host last sent (and from anything we already shipped). Runs before
        /// <see cref="ApplyIncoming"/> so a fresh edit is sent before a stale snapshot
        /// could overwrite it.
        /// </summary>
        private void DetectLocalEdits(MultiplayerSession session)
        {
            long now = _clock.ElapsedMilliseconds;
            if (now - _lastEditScanMs < EditDetectIntervalMs) return;
            _lastEditScanMs = now;

            foreach (byte channelId in _editable)
            {
                // Until the host has told us its state once, "different" means nothing —
                // we may simply still hold pre-join defaults.
                byte[] hostPayload;
                if (!_lastHostPayload.TryGetValue(channelId, out hostPayload)) continue;

                var writer = new NetworkWriter(64);
                if (!_channels[channelId].Capture(EntityManager, writer)) continue;
                byte[] local = writer.ToArray();

                if (BytesEqual(local, hostPayload)) { _pendingEdits.Remove(channelId); continue; }

                PendingEdit pending;
                if (_pendingEdits.TryGetValue(channelId, out pending) &&
                    BytesEqual(local, pending.Payload) &&
                    now - pending.SentMs < EditPendingTimeoutMs)
                    continue; // already in flight

                _pendingEdits[channelId] = new PendingEdit { Payload = local, SentMs = now };
                session.SendStateEdit(channelId, local);
                Mod.Verbose("[MP] CityState: local edit on channel " + channelId + " sent to host.");
            }
        }




        /// <summary>Bridges session callbacks (sim thread) into this system's queues.</summary>
        private sealed class Observer : SessionObserver
        {
            private readonly ConcurrentQueue<StateSnapshotMessage> _snapshots;
            private readonly ConcurrentQueue<StateEditMessage> _edits;

            public Observer(ConcurrentQueue<StateSnapshotMessage> snapshots, ConcurrentQueue<StateEditMessage> edits)
            {
                _snapshots = snapshots;
                _edits = edits;
            }

            public override void OnStateReceived(StateSnapshotMessage snapshot) => SyncInbox.Push(_snapshots, snapshot);
            public override void OnStateEditReceived(StateEditMessage edit) => SyncInbox.Push(_edits, edit);
        }
    }
}
