using System.Collections.Generic;
using Game.Areas;
using Game.Policies;
using Game.Prefabs;
using Game.Routes;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using CS2MultiplayerMod.Core.Protocol.Messages;
using CS2MultiplayerMod.Core.Session;
using CS2MultiplayerMod.Game.Sync.Commands;

namespace CS2MultiplayerMod.Game.Sync.Systems
{
    public partial class PolicySyncSystem
    {
        private void ApplyIncoming(MultiplayerSession session, long now)
        {
            SimulationCommandMessage message;
            while (_incoming.TryDequeue(out message))
            {
                if (message.OriginPlayerId == session.LocalPlayerId) continue;

                EntityPolicyCommand command;
                try { command = EntityPolicyCommand.Decode(message.Body); }
                catch (System.Exception ex) { Mod.log.Warn("[MP] PolicySync: dropping malformed command: " + ex.Message); continue; }

                Entity policy;
                if (!_prefabIndex.TryResolve(command.PolicyPrefabName, out policy))
                {
                    Mod.log.Warn("[MP] PolicySync: unknown policy '" + command.PolicyPrefabName + "'; skipping.");
                    continue;
                }

                var anchor = new float3(command.AnchorX, command.AnchorY, command.AnchorZ);
                Entity target = FindTarget(command.TargetKind, command.TargetPrefabName, anchor);
                if (target == Entity.Null)
                {
                    Mod.log.Warn("[MP] PolicySync: no local " + KindName(command.TargetKind) + " '" +
                                 command.TargetPrefabName + "' near anchor for policy '" +
                                 command.PolicyPrefabName + "'; skipping.");
                    continue;
                }

                _guard.Mark(PolicyKey(command.PolicyPrefabName, command.TargetPrefabName, anchor), now);
                try
                {
                    _policiesUI.SetPolicy(target, policy, command.Active, command.Adjustment);
                    Mod.Verbose("[MP] PolicySync realize: '" + command.PolicyPrefabName + "' " +
                                 (command.Active ? "on" : "off") + " for " + KindName(command.TargetKind) +
                                 " '" + command.TargetPrefabName + "' from player " + message.OriginPlayerId + ".");
                }
                catch (System.Exception ex)
                {
                    Mod.log.Error("[MP] PolicySync realize FAILED for '" + command.PolicyPrefabName + "': " + ex);
                }
            }
        }

        private Entity FindTarget(byte kind, string prefabName, float3 anchor)
        {
            Entity prefab;
            if (!_prefabIndex.TryResolve(prefabName, out prefab)) return Entity.Null;

            EntityQuery query = kind == EntityPolicyCommand.KindDistrict ? _districts :
                                kind == EntityPolicyCommand.KindRoute ? _routes : _buildings;
            // Districts can drift further (their centroid moves when redrawn mid-flight).
            float maxSq = kind == EntityPolicyCommand.KindDistrict ? 250000f :
                          kind == EntityPolicyCommand.KindRoute ? 256f : 16f;

            Entity best = Entity.Null;
            float bestSq = maxSq;
            NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            try
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    if (EntityManager.GetComponentData<PrefabRef>(entities[i]).m_Prefab != prefab) continue;
                    float3 candidate;
                    if (!TryAnchor(kind, entities[i], out candidate)) continue;
                    float d = math.distancesq(candidate, anchor);
                    if (d > bestSq) continue;
                    bestSq = d;
                    best = entities[i];
                }
            }
            finally
            {
                entities.Dispose();
            }
            return best;
        }

        /// <summary>Cross-machine identity per target kind (entity ids differ per machine).</summary>
        private bool TryAnchor(byte kind, Entity entity, out float3 anchor)
        {
            anchor = default;
            switch (kind)
            {
                case EntityPolicyCommand.KindDistrict:
                {
                    DynamicBuffer<Node> nodes = EntityManager.GetBuffer<Node>(entity, true);
                    if (nodes.Length == 0) return false;
                    float3 sum = float3.zero;
                    for (int i = 0; i < nodes.Length; i++) sum += nodes[i].m_Position;
                    anchor = sum / nodes.Length;
                    anchor.y = 0f;
                    return true;
                }
                case EntityPolicyCommand.KindRoute:
                {
                    if (!EntityManager.HasBuffer<RouteWaypoint>(entity)) return false;
                    DynamicBuffer<RouteWaypoint> waypoints = EntityManager.GetBuffer<RouteWaypoint>(entity, true);
                    if (waypoints.Length == 0 ||
                        !EntityManager.HasComponent<Position>(waypoints[0].m_Waypoint)) return false;
                    anchor = EntityManager.GetComponentData<Position>(waypoints[0].m_Waypoint).m_Position;
                    return true;
                }
                default:
                {
                    anchor = EntityManager.GetComponentData<global::Game.Objects.Transform>(entity).m_Position;
                    return true;
                }
            }
        }

        private List<PolicyEntry> ReadPolicies(Entity entity)
        {
            DynamicBuffer<Policy> buffer = EntityManager.GetBuffer<Policy>(entity, true);
            var list = new List<PolicyEntry>(buffer.Length);
            for (int i = 0; i < buffer.Length; i++)
                list.Add(new PolicyEntry
                {
                    Policy = buffer[i].m_Policy,
                    Active = (buffer[i].m_Flags & PolicyFlags.Active) != 0,
                    Adjustment = buffer[i].m_Adjustment,
                });
            return list;
        }

    }
}
