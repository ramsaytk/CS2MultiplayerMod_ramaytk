
using Game;
using Game.Common;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;

using CS2MultiplayerMod.Game.Sync.Systems.Net;
namespace CS2MultiplayerMod.Game.Sync.Systems
{
    /// <summary>
    /// Keeps the local player's tool preview out of an armed net commit.
    ///
    /// The build tools record their preview definitions through <c>ToolOutputBarrier</c>, which
    /// plays them back at the END of ToolUpdate - after the tools, after SyncRealizeSystem, after
    /// ToolOutputSystem. The def-frame wipe (<see cref="NetSyncSystem.PrepareDefinitionFrame"/>)
    /// therefore can never see them: it clears the STANDING preview Temps, but this frame's
    /// definitions materialise right behind it and rebuild the preview into the armed commit
    /// window. The ApplyTool flip one frame later then committed the player's un-applied gesture
    /// along with our batch - a remote player's apply placed the local player's half-drawn
    /// road/railway, planted their hovered building ghost, or (bulldozer out) deleted what they
    /// were pointing at.
    ///
    /// This system is registered directly AFTER ToolOutputBarrier, the one slot where the tool's
    /// definitions exist but have not yet been consumed (Modification runs later this frame). On
    /// frames with an armed commit it destroys them, so the eventual flip commits our batch alone.
    /// Our own definitions are exempt: every feeder tags its definitions Deleted at birth
    /// (self-cleanup at frame end), which the tools never do. Permanent definitions (remote
    /// buildings/upgrades/moves, the game's own zone-growth spawns) never become Temps and are
    /// spared too. On the flip frame itself the commit flags clear before this system runs, so
    /// that frame's definitions live and the preview is back the same frame the batch commits.
    /// </summary>
    public partial class DefinitionGateSystem : GameSystemBase
    {
        private NetSyncSystem _netSync;
        private ToolSystem _toolSystem;
        private EntityQuery _freshToolDefinitions;

        protected override void OnCreate()
        {
            base.OnCreate();
            _netSync = World.GetOrCreateSystemManaged<NetSyncSystem>();
            _toolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            // Fresh (Updated) definitions that are NOT ours - every sync feeder adds Deleted to
            // its definitions at creation, the tools never do.
            _freshToolDefinitions = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<CreationDefinition>(),
                    ComponentType.ReadOnly<Updated>(),
                },
                None = new[] { ComponentType.ReadOnly<Deleted>() },
            });
        }

        protected override void OnUpdate()
        {
            if (!_netSync.HasArmedNetCommit) return;
            if (_freshToolDefinitions.IsEmptyIgnoreFilter) return;

            int wiped = 0;
            NativeArray<Entity> defs = _freshToolDefinitions.ToEntityArray(Allocator.Temp);
            try
            {
                for (int i = 0; i < defs.Length; i++)
                {
                    CreationDefinition def = EntityManager.GetComponentData<CreationDefinition>(defs[i]);
                    if ((def.m_Flags & CreationFlags.Permanent) != 0) continue;
                    EntityManager.DestroyEntity(defs[i]);
                    wiped++;
                }
            }
            finally
            {
                defs.Dispose();
            }
            if (wiped == 0) return;

            // The wiped definitions were the preview a parked cursor would not re-emit; force the
            // tool to regenerate next frame so the gesture stays visible (control points survive
            // in the tool system regardless).
            ToolBaseSystem tool = _toolSystem != null ? _toolSystem.activeTool : null;
            if (tool != null && !(tool is DefaultToolSystem)) _netSync.TryForceToolUpdate(tool);
            Diagnostics.FlightRecorder.Note("def gate wiped defs=" + wiped
                + (tool != null ? " tool=" + tool.GetType().Name : ""));
        }
    }
}
