using System.Collections.Generic;
using Game.Areas;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using CS2MultiplayerMod.Game.Sync.Commands;

namespace CS2MultiplayerMod.Game.Sync.Systems
{
    public partial class AreaSyncSystem
    {
        private void RealizeUpdate(AreaUpdateCommand command, int originPlayerId, long now)
        {
            Entity prefab;
            if (!_prefabIndex.TryResolve(command.PrefabName, out prefab))
            {
                Mod.log.Warn("[MP] AreaSync update: unknown prefab '" + command.PrefabName + "'; skipping.");
                return;
            }
            if (command.NodeX == null || command.NodeX.Length < 3) return;

            // The anchor is the polygon's centroid BEFORE the edit — exactly what our
            // not-yet-edited copy still has. Nearest same-prefab area within 500 m wins.
            var anchor = new float3(command.AnchorX, 0f, command.AnchorZ);
            Entity best = Entity.Null;
            float bestSq = 250000f;
            NativeArray<Entity> entities = _liveAreas.ToEntityArray(Allocator.Temp);
            try
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    if (EntityManager.GetComponentData<PrefabRef>(entities[i]).m_Prefab != prefab) continue;
                    float3[] ring = ReadRing(entities[i]);
                    if (ring.Length == 0) continue;
                    float d = math.distancesq(CentroidOf(ring), anchor);
                    if (d > bestSq) continue;
                    bestSq = d;
                    best = entities[i];
                }
            }
            finally
            {
                entities.Dispose();
            }

            if (best == Entity.Null)
            {
                Mod.log.Warn("[MP] AreaSync update: no local '" + command.PrefabName +
                             "' near the old centroid; skipping redraw.");
                return;
            }

            try
            {
                // Rewrite the ring in place — the entity (and with it district policies,
                // citizen assignments, …) keeps its identity; Updated retriangulates.
                DynamicBuffer<Node> nodes = EntityManager.GetBuffer<Node>(best);
                nodes.Clear();
                var newRing = new float3[command.NodeX.Length];
                for (int n = 0; n < command.NodeX.Length; n++)
                {
                    var position = new float3(command.NodeX[n], command.NodeY[n], command.NodeZ[n]);
                    newRing[n] = position;
                    nodes.Add(new Node { m_Position = position, m_Elevation = command.NodeElevation[n] });
                }
                EntityManager.AddComponent<Updated>(best);

                // Suppress the echo both ways: spatial guard + the scan cache itself.
                _guard.Mark(AreaUpdateKey(command.PrefabName, CentroidOf(newRing)), now);
                _knownRings[best] = newRing;
                Mod.Verbose("[MP] AreaSync update: redrew '" + command.PrefabName + "' (" +
                             command.NodeX.Length + " nodes) from player " + originPlayerId + ".");
            }
            catch (System.Exception ex)
            {
                Mod.log.Error("[MP] AreaSync update FAILED for '" + command.PrefabName + "': " + ex);
            }
        }

        private void RealizeCreate(AreaCreateCommand command, int originPlayerId, long now)
        {
            Entity prefab;
            if (!_prefabIndex.TryResolve(command.PrefabName, out prefab))
            {
                Mod.log.Warn("[MP] AreaSync realize: unknown prefab '" + command.PrefabName + "'; skipping.");
                return;
            }
            if (command.NodeX == null || command.NodeX.Length < 3) return;

            var first = new float3(command.NodeX[0], command.NodeY[0], command.NodeZ[0]);
            _guard.Mark(AreaKey(command.PrefabName, first), now);
            try
            {
                // CreateAreasJob (GenerateAreasSystem) consumes CreationDefinition + a Node
                // ring buffer — same Updated/Deleted definition lifecycle as objects/nets.
                Entity definition = EntityManager.CreateEntity();
                EntityManager.AddComponentData(definition, new CreationDefinition
                {
                    m_Prefab = prefab,
                    m_RandomSeed = 0,
                    m_Flags = CreationFlags.Permanent,
                });
                DynamicBuffer<Node> nodes = EntityManager.AddBuffer<Node>(definition);
                for (int n = 0; n < command.NodeX.Length; n++)
                    nodes.Add(new Node
                    {
                        m_Position = new float3(command.NodeX[n], command.NodeY[n], command.NodeZ[n]),
                        m_Elevation = command.NodeElevation[n],
                    });
                EntityManager.AddComponent<Updated>(definition);
                EntityManager.AddComponent<Deleted>(definition);
                Mod.Verbose("[MP] AreaSync realize: drew '" + command.PrefabName + "' (" +
                             command.NodeX.Length + " nodes) from player " + originPlayerId + ".");
            }
            catch (System.Exception ex)
            {
                Mod.log.Error("[MP] AreaSync realize FAILED for '" + command.PrefabName + "': " + ex);
            }
        }

        private void RealizeDeletes(List<AreaDeleteCommand> commands, long now)
        {
            var targets = new List<(Entity prefab, float3 first, int count, string name)>();
            for (int i = 0; i < commands.Count; i++)
            {
                Entity prefab;
                if (_prefabIndex.TryResolve(commands[i].PrefabName, out prefab))
                    targets.Add((prefab,
                        new float3(commands[i].NodeX, commands[i].NodeY, commands[i].NodeZ),
                        commands[i].NodeCount, commands[i].PrefabName));
            }
            if (targets.Count == 0) return;

            int deleted = 0;
            NativeArray<Entity> entities = _liveAreas.ToEntityArray(Allocator.Temp);
            try
            {
                for (int i = 0; i < entities.Length && targets.Count > 0; i++)
                {
                    Entity candidatePrefab = EntityManager.GetComponentData<PrefabRef>(entities[i]).m_Prefab;
                    DynamicBuffer<Node> nodes = EntityManager.GetBuffer<Node>(entities[i], true);
                    if (nodes.Length == 0) continue;

                    for (int t = targets.Count - 1; t >= 0; t--)
                    {
                        if (targets[t].prefab != candidatePrefab) continue;
                        if (nodes.Length != targets[t].count) continue;
                        if (math.distancesq(targets[t].first, nodes[0].m_Position) > 4f) continue;

                        _guard.Mark(AreaDeleteKey(targets[t].name, nodes[0].m_Position), now);
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
                Mod.Verbose("[MP] AreaSync: removed " + deleted + " area(s); " + targets.Count +
                             " already gone (no local match).");
        }

    }
}
