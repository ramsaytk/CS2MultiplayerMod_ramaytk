using System.Collections.Concurrent;
using System.Collections.Generic;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.Routes;
using Game.Tools;
using Unity.Entities;
using Unity.Mathematics;
using CS2MultiplayerMod.Core.Protocol.Messages;
using CS2MultiplayerMod.Core.Session;

using CS2MultiplayerMod.Game.Sync.Infrastructure;
using CS2MultiplayerMod.Game.Sync.Commands;
namespace CS2MultiplayerMod.Game.Sync.Systems
{
    /// <summary>
    /// Replicates transit lines (bus/tram/metro/train routes) in both directions. A line
    /// is a <see cref="Route"/> entity owning a ring of waypoint entities; the line
    /// travels as the route prefab name + the ordered waypoint positions, and the
    /// receiver rebuilds it via the <see cref="CreationDefinition"/> +
    /// <see cref="WaypointDefinition"/> buffer that <c>GenerateRoutesSystem.CreateRoutesJob</c>
    /// consumes (verified by dump).
    ///
    /// Cross-machine identity: route NUMBERS diverge per machine (each game's
    /// InitializeSystem hands them out locally), so lines are matched by their first
    /// waypoint's position, never by number.
    ///
    /// Post-creation edits are live too: a 1 Hz scan compares each line's waypoint ring
    /// and color against what we last saw; a change becomes a <see cref="RouteUpdateCommand"/>
    /// anchored at the OLD first waypoint. A pure recolor is applied directly to the
    /// matched route's <see cref="Color"/> component; a stop change rebuilds the line
    /// through the game's definition pipeline with <c>m_Original</c> set, the same way
    /// the transport line tool commits an edit.
    /// </summary>
    public partial class RouteSyncSystem : GameSystemBase
    {
        private const long EditScanIntervalMs = 1000;

        private readonly ConcurrentQueue<SimulationCommandMessage> _incoming =
            new ConcurrentQueue<SimulationCommandMessage>();
        private readonly ReplicationGuard _guard = new ReplicationGuard();
        private Dictionary<Entity, RouteSnapshot> _knownRoutes = new Dictionary<Entity, RouteSnapshot>();
        private Dictionary<Entity, RouteSnapshot> _nextRoutes = new Dictionary<Entity, RouteSnapshot>();
        private long _lastEditScanMs;

        private struct RouteSnapshot
        {
            public float3[] Ring;
            public uint Rgba;
        }

        private PrefabSystem _prefabSystem;
        private PrefabIndex _prefabIndex;
        private EntityQuery _createdRoutes;
        private EntityQuery _deletedRoutes;
        private EntityQuery _liveRoutes;
        private CommandObserver _observer;

        protected override void OnCreate()
        {
            base.OnCreate();

            Mod.log.Info(nameof(RouteSyncSystem) + " ready.");
            _prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            _prefabIndex = new PrefabIndex(_prefabSystem, GetEntityQuery(ComponentType.ReadOnly<PrefabData>()));

            _createdRoutes = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Created>(),
                    ComponentType.ReadOnly<Route>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<Deleted>(),
                },
            });

            _deletedRoutes = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Route>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Temp>(),
                },
            });

            _liveRoutes = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Route>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<Deleted>(),
                },
            });

            if (Mod.Service != null)
            {
                _observer = new CommandObserver(_incoming, RouteCreateCommand.Id, RouteUpdateCommand.Id, RouteDeleteCommand.Id);
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
                if (_knownRoutes.Count > 0) _knownRoutes.Clear();
                return;
            }

            long now = service.NowMs;
            _guard.Prune(now);
            CaptureCreated(session, now);
            CaptureDeleted(session, now);
            ScanForEdits(session, now);
        }

        /// <summary>Called by <see cref="SyncRealizeSystem"/> during ToolUpdate (see there for why).</summary>
        public void RealizePending()
        {
            MultiplayerService service = Mod.Service;
            if (service == null) return;

            MultiplayerSession session = service.Session;
            if (!service.GameplaySyncReady) return;

            List<RouteDeleteCommand> deletes = null;
            long now = service.NowMs;
            SimulationCommandMessage message;
            while (_incoming.TryDequeue(out message))
            {
                if (message.OriginPlayerId == session.LocalPlayerId) continue;
                try
                {
                    if (message.CommandId == RouteCreateCommand.Id)
                        RealizeCreate(RouteCreateCommand.Decode(message.Body), message.OriginPlayerId, now);
                    else if (message.CommandId == RouteUpdateCommand.Id)
                        RealizeUpdate(RouteUpdateCommand.Decode(message.Body), message.OriginPlayerId, now);
                    else if (message.CommandId == RouteDeleteCommand.Id)
                        (deletes ?? (deletes = new List<RouteDeleteCommand>())).Add(RouteDeleteCommand.Decode(message.Body));
                }
                catch (System.Exception ex) { Mod.log.Warn("[MP] RouteSync: dropping malformed command: " + ex.Message); }
            }
            if (deletes != null) RealizeDeletes(deletes, now);
        }





        // ---- Line edits (stops / color) ----------------------------------------






        private static string RouteKey(string prefabName, float3 firstWaypoint) =>
            "route|" + ReplicationGuard.Key(prefabName, firstWaypoint);

        private static string RouteDeleteKey(string prefabName, float3 firstWaypoint) =>
            "routedel|" + ReplicationGuard.Key(prefabName, firstWaypoint);

        private static string RouteUpdateKey(string prefabName, float3 firstWaypoint) =>
            "routeupd|" + ReplicationGuard.Key(prefabName, firstWaypoint);

    }
}
