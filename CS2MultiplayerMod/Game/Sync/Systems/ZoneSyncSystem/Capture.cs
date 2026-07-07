using System.Collections.Generic;
using Game.Zones;
using Unity.Collections;
using Unity.Entities;
using CS2MultiplayerMod.Core.Session;
using CS2MultiplayerMod.Game.Sync.Commands;

namespace CS2MultiplayerMod.Game.Sync.Systems
{
    public partial class ZoneSyncSystem
    {
        private void CaptureUpdatedBlocks(MultiplayerSession session, long now)
        {
            if (_updatedBlocks.IsEmptyIgnoreFilter) return;

            NativeArray<Entity> blocks = _updatedBlocks.ToEntityArray(Allocator.Temp);
            try
            {
                for (int i = 0; i < blocks.Length; i++)
                {
                    Block block = EntityManager.GetComponentData<Block>(blocks[i]);
                    DynamicBuffer<Cell> cells = EntityManager.GetBuffer<Cell>(blocks[i], true);

                    // Blocks update for many reasons (road edits, sim) — only zoned blocks
                    // are worth broadcasting; an all-None block that was just unzoned still
                    // carries names=0 + non-empty cells, which the receiver applies fine.
                    var names = new List<string>();
                    var cellBytes = new byte[cells.Length];
                    bool anyZoned = false;
                    for (int c = 0; c < cells.Length; c++)
                    {
                        ushort zoneIndex = cells[c].m_Zone.m_Index;
                        if (zoneIndex == 0)
                        {
                            cellBytes[c] = ZonePaintCommand.NoneCell;
                            continue;
                        }

                        string zoneName = ResolveZoneName(zoneIndex);
                        if (zoneName == null)
                        {
                            cellBytes[c] = ZonePaintCommand.NoneCell;
                            continue;
                        }

                        int tableIndex = names.IndexOf(zoneName);
                        if (tableIndex < 0)
                        {
                            if (names.Count >= ZonePaintCommand.NoneCell) continue; // table full — never in practice
                            names.Add(zoneName);
                            tableIndex = names.Count - 1;
                        }
                        cellBytes[c] = (byte)tableIndex;
                        anyZoned = true;
                    }

                    // Untouched-by-zoning blocks churn constantly (road rebuilding etc.);
                    // skip them unless we previously synced content for this block.
                    string key = BlockKey(block.m_Position, ContentHash(names, cellBytes));
                    if (_guard.Consume(key, now)) continue;
                    if (!anyZoned && !_zonedBlocks.Contains(QuantizedPos(block.m_Position))) continue;
                    _zonedBlocks.Add(QuantizedPos(block.m_Position));

                    var command = new ZonePaintCommand
                    {
                        PosX = block.m_Position.x,
                        PosY = block.m_Position.y,
                        PosZ = block.m_Position.z,
                        ZoneNames = names.ToArray(),
                        Cells = cellBytes,
                    };
                    session.SendCommand(0, ZonePaintCommand.Id, command.Encode());
                }
            }
            finally
            {
                blocks.Dispose();
            }
        }

        private static int ContentHash(List<string> names, byte[] cells)
        {
            unchecked
            {
                int hash = (int)2166136261;
                for (int i = 0; i < cells.Length; i++)
                {
                    // Hash the NAME of each cell's zone, not the table index, so identical
                    // zoning hashes identically regardless of table order.
                    string name = cells[i] != ZonePaintCommand.NoneCell && cells[i] < names.Count
                        ? names[cells[i]] : "";
                    for (int c = 0; c < name.Length; c++) hash = (hash ^ name[c]) * 16777619;
                    hash = (hash ^ '|') * 16777619;
                }
                return hash;
            }
        }

    }
}
