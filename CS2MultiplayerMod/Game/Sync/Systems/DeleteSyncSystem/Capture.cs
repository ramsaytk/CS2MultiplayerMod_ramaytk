using Colossal.Mathematics;
using Game.Net;
using Game.Objects;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using CS2MultiplayerMod.Core.Session;
using CS2MultiplayerMod.Game.Sync.Commands;
using CS2MultiplayerMod.Game.Sync.Infrastructure;

namespace CS2MultiplayerMod.Game.Sync.Systems
{
    public partial class DeleteSyncSystem
    {
        private void CaptureDeletedObjects(MultiplayerSession session, long now)
        {
            if (_deletedObjects.IsEmptyIgnoreFilter) return;

            NativeArray<Entity> entities = _deletedObjects.ToEntityArray(Allocator.Temp);
            try
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    Entity prefab = EntityManager.GetComponentData<PrefabRef>(entities[i]).m_Prefab;
                    string name = _prefabSystem.GetPrefabName(prefab);
                    if (string.IsNullOrEmpty(name)) continue;

                    float3 pos = EntityManager.GetComponentData<Transform>(entities[i]).m_Position;
                    if (_guard.Consume(DeleteKey(name, pos), now)) continue;

                    var command = new ObjectDeleteCommand
                    {
                        PrefabName = name,
                        PosX = pos.x, PosY = pos.y, PosZ = pos.z,
                    };
                    session.SendCommand(0, ObjectDeleteCommand.Id, command.Encode());
                }
            }
            finally
            {
                entities.Dispose();
            }
        }

        private void CaptureDeletedEdges(MultiplayerSession session, long now)
        {
            if (_deletedEdges.IsEmptyIgnoreFilter) return;

            // Snapshot this frame's Created edges so we can distinguish a mid-span SPLIT from a real
            // bulldoze. A split deletes the original edge and creates two halves on its centreline;
            // replicating that delete would tear down the receiver's still-whole edge before its own
            // local split runs, leaving the new road disconnected ("not accessible"). So below we skip
            // deleting an edge whose same-prefab Created halves lie on its centreline in 3D — the
            // receiver reproduces the split locally from the drawn-edge command instead. Pieces that
            // follow the centreline only in XZ but at a different HEIGHT mean the span was REBUILT at
            // a new elevation (the raise/lower gesture, also committed as delete + create): that
            // delete IS sent, and NetSyncSystem sends the rebuilt pieces one frame behind it.
            NativeArray<Entity> createdEnts = _createdEdges.ToEntityArray(Allocator.Temp);
            NativeArray<Curve> createdCurves = _createdEdges.ToComponentDataArray<Curve>(Allocator.Temp);
            var createdPrefabs = new NativeArray<Entity>(createdEnts.Length, Allocator.Temp);
            for (int i = 0; i < createdEnts.Length; i++)
                createdPrefabs[i] = EntityManager.GetComponentData<PrefabRef>(createdEnts[i]).m_Prefab;

            // This frame's geometry-changed survivors, for the node-reduction test below. When a
            // bulldoze frees a node between two collinear same-prefab edges, the game merges them:
            // one neighbour is committed with the JOINED curve (Updated, covers the other's span),
            // the other is Deleted. Replicating that victim's delete would rip half the through-road
            // out of a receiver whose own reduction hasn't run yet (its own commit of the bulldoze
            // reproduces the merge natively) — the "street half-deleted / stub left behind" bug.
            NativeArray<Entity> updatedEnts = _updatedEdges.ToEntityArray(Allocator.Temp);
            NativeArray<Curve> updatedCurves = _updatedEdges.ToComponentDataArray<Curve>(Allocator.Temp);
            var updatedPrefabs = new NativeArray<Entity>(updatedEnts.Length, Allocator.Temp);
            for (int i = 0; i < updatedEnts.Length; i++)
                updatedPrefabs[i] = EntityManager.GetComponentData<PrefabRef>(updatedEnts[i]).m_Prefab;

            NativeArray<Entity> entities = _deletedEdges.ToEntityArray(Allocator.Temp);
            try
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    Entity prefab = EntityManager.GetComponentData<PrefabRef>(entities[i]).m_Prefab;
                    string name = _prefabSystem.GetPrefabName(prefab);
                    if (string.IsNullOrEmpty(name) || name.StartsWith("Invisible")) continue;

                    Bezier4x3 b = EntityManager.GetComponentData<Curve>(entities[i]).m_Bezier;
                    string xzA = b.a.x.ToString("F1") + "," + b.a.z.ToString("F1");
                    string xzD = b.d.x.ToString("F1") + "," + b.d.z.ToString("F1");

                    // A node-reduction victim, not a bulldoze — a same-prefab neighbour was extended
                    // over this edge's span this same frame. The receiver's own commit reproduces the
                    // merge, so this delete stays local.
                    if (IsReductionVictim(b, prefab, updatedPrefabs, updatedCurves))
                    {
                        Mod.NetTrace("delete SKIP reduction-victim '" + name + "' (" + xzA + "→" + xzD +
                                     ") — merged into an extended neighbour, receiver reduces locally.");
                        continue;
                    }

                    // A split, not a bulldoze — let the receiver split its own copy locally.
                    if (IsBeingSplit(b, prefab, createdPrefabs, createdCurves))
                    {
                        Mod.NetTrace("delete SKIP split '" + name + "' (" + xzA + "→" + xzD +
                                     ") — mid-span split, receiver splits locally (NOT a bulldoze).");
                        continue;
                    }

                    if (_guard.Consume(DeleteKey(name, b.a), now))
                    {
                        Mod.NetTrace("delete skip ECHO '" + name + "' at " + xzA + ".");
                        continue;
                    }

                    var command = new NetDeleteCommand
                    {
                        PrefabName = name,
                        Ax = b.a.x, Ay = b.a.y, Az = b.a.z,
                        Bx = b.b.x, By = b.b.y, Bz = b.b.z,
                        Cx = b.c.x, Cy = b.c.y, Cz = b.c.z,
                        Dx = b.d.x, Dy = b.d.y, Dz = b.d.z,
                    };
                    session.SendCommand(0, NetDeleteCommand.Id, command.Encode());
                    Mod.NetTrace("LOCAL BULLDOZE edge → SENT delete '" + name + "' (" + xzA + "→" + xzD + ").");
                }
            }
            finally
            {
                entities.Dispose();
                createdEnts.Dispose();
                createdCurves.Dispose();
                createdPrefabs.Dispose();
                updatedEnts.Dispose();
                updatedCurves.Dispose();
                updatedPrefabs.Dispose();
            }
        }

        /// <summary>
        /// True when <paramref name="deleted"/> died to a node reduction rather than a bulldoze: a
        /// same-prefab edge whose geometry changed this same frame (Updated, not Created) now covers
        /// its span in 3D — the game joined the two edges and this one is the leftover. The joined
        /// curve tracks each merged half within ~0.1 m, far inside the SplitMatch tolerances.
        /// </summary>
        private static bool IsReductionVictim(Bezier4x3 deleted, Entity prefab,
            NativeArray<Entity> updatedPrefabs, NativeArray<Curve> updatedCurves)
        {
            for (int i = 0; i < updatedCurves.Length; i++)
            {
                if (updatedPrefabs[i] != prefab) continue;
                if (SplitMatch.IsSubCurve3D(deleted, updatedCurves[i].m_Bezier)) return true;
            }
            return false;
        }

        /// <summary>
        /// True when <paramref name="deleted"/> is being SPLIT rather than bulldozed or rebuilt: every
        /// same-prefab edge Created this frame along its centreline matches its height too (a true 3D
        /// sub-curve — one of the split halves). The receiver reproduces a split locally, so that
        /// delete is not replicated. If any piece follows the path at a DIFFERENT height, the span is
        /// being rebuilt at a new elevation (the raise/lower gesture) — that delete IS replicated,
        /// with the rebuilt pieces following one frame later (see <c>NetSyncSystem</c>, which runs the
        /// same test on the same frame's data).
        /// </summary>
        private static bool IsBeingSplit(Bezier4x3 deleted, Entity prefab,
            NativeArray<Entity> createdPrefabs, NativeArray<Curve> createdCurves)
        {
            bool covered = false;
            for (int i = 0; i < createdCurves.Length; i++)
            {
                if (createdPrefabs[i] != prefab) continue;
                Bezier4x3 c = createdCurves[i].m_Bezier;
                if (!SplitMatch.FollowsXZ(c, deleted)) continue;
                if (!SplitMatch.HeightMatches(c, deleted)) return false; // rebuilt at a new height
                covered = true;
            }
            return covered;
        }

    }
}
