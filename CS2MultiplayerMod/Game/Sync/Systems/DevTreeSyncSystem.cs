using System.Collections.Concurrent;
using System.Collections.Generic;
using Game;
using Game.City;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using CS2MultiplayerMod.Core.Protocol.Messages;
using CS2MultiplayerMod.Core.Session;

using CS2MultiplayerMod.Game.Sync.Infrastructure;
using CS2MultiplayerMod.Game.Sync.Commands;
using CS2MultiplayerMod.Game.Sync.Channels;
namespace CS2MultiplayerMod.Game.Sync.Systems
{
    /// <summary>
    /// Replicates development-tree node purchases. The points *count* was already
    /// replicated (authoritative, host → client) by <see cref="DevTreePointsStateChannel"/>,
    /// but the unlock itself was not — so a partner's tree never updated. Worse, because a
    /// client's local spend was overwritten by the host's points snapshot (the host never
    /// learned of the purchase, so it never deducted), the client's points were refilled
    /// every second: effectively infinite unlocks.
    ///
    ///   detect: a <see cref="DevTreeNodeData"/> node whose enabled <see cref="Locked"/>
    ///           tag just cleared, and that we did not unlock from a remote command, was
    ///           purchased locally → broadcast a <see cref="DevTreePurchaseCommand"/>
    ///           (node by prefab name).
    ///   apply: resolve the node by name; if still locked, raise the same unlock event the
    ///          game uses — but via the <see cref="EndFrameBarrier"/>, exactly as
    ///          <c>DevTreeSystem.Purchase</c> does. This timing is load-bearing: the game's
    ///          <c>UnlockSystem</c> consumes <c>Unlock</c> events at <c>MainLoop</c> (the
    ///          first phase of the frame) and <c>CleanUpSystem</c> destroys every event
    ///          entity at <c>Cleanup</c> (the last). This system runs at <c>UIUpdate</c>, so
    ///          an event created directly here would be reaped that same frame before
    ///          <c>UnlockSystem</c> ever saw it — the points dropped but the unlock never
    ///          landed on the partner. Deferring to the barrier makes the event appear at the
    ///          next <c>MainLoop</c>, where <c>UnlockSystem</c> picks it up and cascades the
    ///          dependent content unlocks. The host additionally subtracts the node's cost
    ///          from the shared <see cref="DevTreePoints"/> — that is what stops the snapshot
    ///          from refilling a client that paid. Clients take their points from the host's
    ///          snapshot.
    ///
    /// The session relays a client's command to the other clients automatically, so the
    /// host need not re-broadcast what it applies; an echo guard suppresses re-detecting an
    /// unlock we just applied.
    /// </summary>
    public partial class DevTreeSyncSystem : GameSystemBase
    {
        private readonly ConcurrentQueue<SimulationCommandMessage> _incoming =
            new ConcurrentQueue<SimulationCommandMessage>();
        private readonly ReplicationGuard _guard = new ReplicationGuard();
        private readonly HashSet<string> _knownUnlocked = new HashSet<string>();
        private readonly Dictionary<string, Entity> _nodeByName = new Dictionary<string, Entity>();

        private PrefabSystem _prefabSystem;
        private EndFrameBarrier _endFrameBarrier;
        private EntityQuery _nodes;
        private EntityQuery _pointsQuery;
        private EntityArchetype _unlockArchetype;
        private CommandObserver _observer;
        private bool _initialized;

        protected override void OnCreate()
        {
            base.OnCreate();
            Mod.log.Info(nameof(DevTreeSyncSystem) + " ready.");

            _prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            // Unlock events must be raised through the same barrier the game uses so
            // UnlockSystem (MainLoop) consumes them before CleanUpSystem reaps them.
            _endFrameBarrier = World.GetOrCreateSystemManaged<EndFrameBarrier>();
            // DevTree nodes are prefab entities — IncludePrefab so the query finds them.
            _nodes = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<DevTreeNodeData>() },
                None = new[] { ComponentType.ReadOnly<Temp>() },
                Options = EntityQueryOptions.IncludePrefab,
            });
            _pointsQuery = GetEntityQuery(ComponentType.ReadWrite<DevTreePoints>());
            // The exact archetype the game raises to unlock a node (see DevTreeSystem).
            _unlockArchetype = EntityManager.CreateArchetype(
                ComponentType.ReadWrite<Unlock>(), ComponentType.ReadWrite<global::Game.Common.Event>());

            if (Mod.Service != null)
            {
                _observer = new CommandObserver(_incoming, DevTreePurchaseCommand.Id);
                Mod.Service.Session.AddObserver(_observer);
            }
        }

        protected override void OnDestroy()
        {
            if (_observer != null && Mod.Service != null)
                Mod.Service.Session.RemoveObserver(_observer);
            base.OnDestroy();
        }

        protected override void OnUpdate()
        {
            MultiplayerService service = Mod.Service;
            if (service == null) return;

            MultiplayerSession session = service.Session;
            if (!service.GameplaySyncReady)
            {
                _initialized = false;
                return;
            }

            long now = service.NowMs;
            _guard.Prune(now);

            // Apply remote purchases first so their unlocks are accounted for before we
            // diff for local ones.
            ApplyIncoming(session, now);

            // First ready tick: adopt the current unlocked set as the baseline so the
            // already-unlocked nodes from the loaded save are never re-broadcast.
            if (!_initialized)
            {
                SeedKnown();
                _initialized = true;
                return;
            }

            DetectLocalPurchases(session, now);
        }

        private bool IsLocked(Entity node) =>
            EntityManager.HasComponent<Locked>(node) && EntityManager.IsComponentEnabled<Locked>(node);

        private void SeedKnown()
        {
            _knownUnlocked.Clear();
            NativeArray<Entity> nodes = _nodes.ToEntityArray(Allocator.Temp);
            try
            {
                for (int i = 0; i < nodes.Length; i++)
                {
                    if (IsLocked(nodes[i])) continue;
                    string name = _prefabSystem.GetPrefabName(nodes[i]);
                    if (!string.IsNullOrEmpty(name)) _knownUnlocked.Add(name);
                }
            }
            finally { nodes.Dispose(); }
        }

        private void DetectLocalPurchases(MultiplayerSession session, long now)
        {
            NativeArray<Entity> nodes = _nodes.ToEntityArray(Allocator.Temp);
            try
            {
                for (int i = 0; i < nodes.Length; i++)
                {
                    string name = _prefabSystem.GetPrefabName(nodes[i]);
                    if (string.IsNullOrEmpty(name)) continue;

                    bool unlocked = !IsLocked(nodes[i]);
                    bool known = _knownUnlocked.Contains(name);

                    if (unlocked && !known)
                    {
                        _knownUnlocked.Add(name);
                        if (_guard.Consume(NodeKey(name), now)) continue; // we applied it — no echo

                        var command = new DevTreePurchaseCommand { NodePrefabName = name };
                        session.SendCommand(0, DevTreePurchaseCommand.Id, command.Encode());
                        Mod.Verbose("[MP] DevTreeSync: broadcast purchase of '" + name + "'.");
                    }
                    else if (!unlocked && known)
                    {
                        // Re-locked (a world resync reloaded the host's state) — let a
                        // future unlock be detected again.
                        _knownUnlocked.Remove(name);
                    }
                }
            }
            finally { nodes.Dispose(); }
        }

        private void ApplyIncoming(MultiplayerSession session, long now)
        {
            SimulationCommandMessage message;
            while (_incoming.TryDequeue(out message))
            {
                if (message.OriginPlayerId == session.LocalPlayerId) continue;

                DevTreePurchaseCommand command;
                try { command = DevTreePurchaseCommand.Decode(message.Body); }
                catch (System.Exception ex) { Mod.log.Warn("[MP] DevTreeSync: dropping malformed command: " + ex.Message); continue; }

                Entity node = ResolveNode(command.NodePrefabName);
                if (node == Entity.Null)
                {
                    Mod.log.Warn("[MP] DevTreeSync: unknown node '" + command.NodePrefabName +
                                 "' from player " + message.OriginPlayerId + "; skipping.");
                    continue;
                }
                if (!IsLocked(node)) continue; // already unlocked here — nothing to do

                _guard.Mark(NodeKey(command.NodePrefabName), now);

                // Unlock the node everywhere so the partner's tree updates. Defer the event
                // to the EndFrameBarrier — creating it directly from UIUpdate would have it
                // reaped by CleanUpSystem this same frame, before UnlockSystem (MainLoop)
                // could process it. The barrier replays it at the next MainLoop where the
                // game's own unlock pipeline (node + dependent-content cascade) runs.
                EntityCommandBuffer ecb = _endFrameBarrier.CreateCommandBuffer();
                Entity e = ecb.CreateEntity(_unlockArchetype);
                ecb.SetComponent(e, new Unlock(node));

                // Only the host owns the points: charge the node's cost so the authoritative
                // snapshot reflects the spend instead of refilling the buyer.
                if (session.Role == SessionRole.Host &&
                    EntityManager.HasComponent<DevTreeNodeData>(node) &&
                    !_pointsQuery.IsEmptyIgnoreFilter)
                {
                    int cost = EntityManager.GetComponentData<DevTreeNodeData>(node).m_Cost;
                    DevTreePoints points = _pointsQuery.GetSingleton<DevTreePoints>();
                    points.m_Points -= cost;
                    _pointsQuery.SetSingleton(points);
                }

                Mod.Verbose("[MP] DevTreeSync: applied purchase of '" + command.NodePrefabName +
                             "' from player " + message.OriginPlayerId + ".");
            }
        }

        private Entity ResolveNode(string name)
        {
            if (string.IsNullOrEmpty(name)) return Entity.Null;

            Entity cached;
            if (_nodeByName.TryGetValue(name, out cached) && EntityManager.Exists(cached)) return cached;

            NativeArray<Entity> nodes = _nodes.ToEntityArray(Allocator.Temp);
            try
            {
                for (int i = 0; i < nodes.Length; i++)
                {
                    string candidate = _prefabSystem.GetPrefabName(nodes[i]);
                    if (!string.IsNullOrEmpty(candidate)) _nodeByName[candidate] = nodes[i];
                }
            }
            finally { nodes.Dispose(); }

            return _nodeByName.TryGetValue(name, out cached) ? cached : Entity.Null;
        }

        private static string NodeKey(string name) => "devtree|" + name;

    }
}
