using System.Collections.Generic;
using Game.Common;
using Game.Prefabs;
using Game.Routes;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using CS2MultiplayerMod.Game.Sync.Commands;

namespace CS2MultiplayerMod.Game.Sync.Systems
{
    public partial class RouteSyncSystem
    {
        private void RealizeCreate(RouteCreateCommand command, int originPlayerId, long now)
        {
            Entity prefab;
            if (!_prefabIndex.TryResolve(command.PrefabName, out prefab))
            {
                Mod.log.Warn("[MP] RouteSync realize: unknown prefab '" + command.PrefabName + "'; skipping.");
                return;
            }
            if (command.WaypointX == null || command.WaypointX.Length < 2) return;

            var first = new float3(command.WaypointX[0], command.WaypointY[0], command.WaypointZ[0]);
            _guard.Mark(RouteKey(command.PrefabName, first), now);
            try
            {
                // CreateRoutesJob (GenerateRoutesSystem) consumes CreationDefinition + a
                // WaypointDefinition buffer. m_Connection/m_Original stay null — the game
                // snaps each waypoint to the stop/track at that position itself. Numbers
                // are assigned locally by InitializeSystem (they diverge by design); the
                // color travels via ColorDefinition when the source line had one.
                Entity definition = EntityManager.CreateEntity();
                EntityManager.AddComponentData(definition, new CreationDefinition
                {
                    m_Prefab = prefab,
                    m_RandomSeed = 0,
                    m_Flags = CreationFlags.Permanent,
                });
                DynamicBuffer<WaypointDefinition> waypoints = EntityManager.AddBuffer<WaypointDefinition>(definition);
                for (int w = 0; w < command.WaypointX.Length; w++)
                    waypoints.Add(new WaypointDefinition
                    {
                        m_Position = new float3(command.WaypointX[w], command.WaypointY[w], command.WaypointZ[w]),
                        m_Connection = Entity.Null,
                        m_Original = Entity.Null,
                    });
                if (command.ColorA != 0)
                    EntityManager.AddComponentData(definition, new ColorDefinition
                    {
                        m_Color = new UnityEngine.Color32(command.ColorR, command.ColorG, command.ColorB, command.ColorA),
                    });
                EntityManager.AddComponent<Updated>(definition);
                EntityManager.AddComponent<Deleted>(definition);
                Mod.Verbose("[MP] RouteSync realize: created line '" + command.PrefabName + "' (" +
                             command.WaypointX.Length + " stops) from player " + originPlayerId + ".");
            }
            catch (System.Exception ex)
            {
                Mod.log.Error("[MP] RouteSync realize FAILED for '" + command.PrefabName + "': " + ex);
            }
        }

        private void RealizeUpdate(RouteUpdateCommand command, int originPlayerId, long now)
        {
            Entity prefab;
            if (!_prefabIndex.TryResolve(command.PrefabName, out prefab))
            {
                Mod.log.Warn("[MP] RouteSync update: unknown prefab '" + command.PrefabName + "'; skipping.");
                return;
            }
            if (command.WaypointX == null || command.WaypointX.Length < 2) return;

            // The anchor is the line's first waypoint BEFORE the edit — what our copy has.
            var anchor = new float3(command.AnchorX, command.AnchorY, command.AnchorZ);
            Entity route = Entity.Null;
            float3[] localRing = null;
            float bestSq = 256f;
            NativeArray<Entity> entities = _liveRoutes.ToEntityArray(Allocator.Temp);
            try
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    if (EntityManager.GetComponentData<PrefabRef>(entities[i]).m_Prefab != prefab) continue;
                    float3[] ring = WaypointPositions(entities[i]);
                    if (ring == null) continue;
                    float d = math.distancesq(ring[0], anchor);
                    if (d > bestSq) continue;
                    bestSq = d;
                    route = entities[i];
                    localRing = ring;
                }
            }
            finally
            {
                entities.Dispose();
            }

            if (route == Entity.Null)
            {
                Mod.log.Warn("[MP] RouteSync update: no local '" + command.PrefabName +
                             "' near the old first waypoint; skipping edit.");
                return;
            }

            var newRing = new float3[command.WaypointX.Length];
            for (int w = 0; w < newRing.Length; w++)
                newRing[w] = new float3(command.WaypointX[w], command.WaypointY[w], command.WaypointZ[w]);
            var color = new UnityEngine.Color32(command.ColorR, command.ColorG, command.ColorB, command.ColorA);

            try
            {
                if (RingsEqual(localRing, newRing))
                {
                    // Stops unchanged → a pure recolor; write the component directly.
                    if (EntityManager.HasComponent<Color>(route))
                        EntityManager.SetComponentData(route, new Color { m_Color = color });
                    else
                        EntityManager.AddComponentData(route, new Color { m_Color = color });
                    EntityManager.AddComponent<Updated>(route);
                }
                else
                {
                    // Stop change → rebuild through the definition pipeline with
                    // m_Original set, the same way the transport line tool commits edits.
                    Entity definition = EntityManager.CreateEntity();
                    EntityManager.AddComponentData(definition, new CreationDefinition
                    {
                        m_Prefab = prefab,
                        m_Original = route,
                        m_RandomSeed = 0,
                        m_Flags = CreationFlags.Permanent,
                    });
                    DynamicBuffer<WaypointDefinition> waypoints = EntityManager.AddBuffer<WaypointDefinition>(definition);
                    for (int w = 0; w < newRing.Length; w++)
                        waypoints.Add(new WaypointDefinition
                        {
                            m_Position = newRing[w],
                            m_Connection = Entity.Null,
                            m_Original = Entity.Null,
                        });
                    if (command.ColorA != 0)
                        EntityManager.AddComponentData(definition, new ColorDefinition { m_Color = color });
                    EntityManager.AddComponent<Updated>(definition);
                    EntityManager.AddComponent<Deleted>(definition);

                    // The rebuild may surface as Created/Deleted on this machine —
                    // make sure none of those echo back as create/delete commands.
                    _guard.Mark(RouteKey(command.PrefabName, newRing[0]), now);
                    _guard.Mark(RouteDeleteKey(command.PrefabName, localRing[0]), now);
                }

                _guard.Mark(RouteUpdateKey(command.PrefabName, newRing[0]), now);
                _knownRoutes[route] = new RouteSnapshot
                {
                    Ring = newRing,
                    Rgba = (uint)(color.r | (color.g << 8) | (color.b << 16) | (color.a << 24)),
                };
                Mod.Verbose("[MP] RouteSync update: edited line '" + command.PrefabName + "' (" +
                             newRing.Length + " stops) from player " + originPlayerId + ".");
            }
            catch (System.Exception ex)
            {
                Mod.log.Error("[MP] RouteSync update FAILED for '" + command.PrefabName + "': " + ex);
            }
        }

        private void RealizeDeletes(List<RouteDeleteCommand> commands, long now)
        {
            var targets = new List<(Entity prefab, float3 first, string name)>();
            for (int i = 0; i < commands.Count; i++)
            {
                Entity prefab;
                if (_prefabIndex.TryResolve(commands[i].PrefabName, out prefab))
                    targets.Add((prefab,
                        new float3(commands[i].WaypointX, commands[i].WaypointY, commands[i].WaypointZ),
                        commands[i].PrefabName));
            }
            if (targets.Count == 0) return;

            int deleted = 0;
            NativeArray<Entity> entities = _liveRoutes.ToEntityArray(Allocator.Temp);
            try
            {
                for (int i = 0; i < entities.Length && targets.Count > 0; i++)
                {
                    Entity candidatePrefab = EntityManager.GetComponentData<PrefabRef>(entities[i]).m_Prefab;
                    float3[] positions = null;

                    for (int t = targets.Count - 1; t >= 0; t--)
                    {
                        if (targets[t].prefab != candidatePrefab) continue;
                        if (positions == null && (positions = WaypointPositions(entities[i])) == null) break;
                        if (math.distancesq(targets[t].first, positions[0]) > 16f) continue;

                        // Marking the route Deleted cascades to its owned waypoints/vehicles
                        // through the game's own teardown, like a local line deletion.
                        _guard.Mark(RouteDeleteKey(targets[t].name, positions[0]), now);
                        EntityManager.AddComponent<Deleted>(entities[i]);
                        targets.RemoveAt(t);
                        deleted++;
                        break;
                    }
                }
            }
            finally
            {
                entities.Dispose();
            }

            if (deleted > 0 || targets.Count > 0)
                Mod.Verbose("[MP] RouteSync: removed " + deleted + " line(s); " + targets.Count +
                             " already gone (no local match).");
        }

    }
}
