using CS2MultiplayerMod.Core.Protocol;
using CS2MultiplayerMod.Core.Sync;

namespace CS2MultiplayerMod.Game.Sync.Commands
{
    /// <summary>
    /// "The zoning of this block now looks like this." Zone cells live in per-road-edge
    /// Block entities; the block is identified by its world position (block layout is
    /// derived deterministically from road geometry, which is itself synced). Zone types
    /// are carried as prefab names via a small per-message string table, because
    /// <c>ZoneType.m_Index</c> is a per-machine value: cell bytes index into the table,
    /// 0xFF = unzoned. See <see cref="ZoneSyncSystem"/>.
    /// </summary>
    public sealed class ZonePaintCommand : ISimulationCommand
    {
        public const ushort Id = 5;
        public const byte NoneCell = 0xFF;

        public float PosX, PosY, PosZ;
        public string[] ZoneNames;
        public byte[] Cells;

        public ushort CommandId => Id;

        public void Write(NetworkWriter writer)
        {
            writer.WriteFloat(PosX);
            writer.WriteFloat(PosY);
            writer.WriteFloat(PosZ);
            writer.WriteByte((byte)(ZoneNames == null ? 0 : ZoneNames.Length));
            if (ZoneNames != null)
                for (int i = 0; i < ZoneNames.Length; i++) writer.WriteString(ZoneNames[i]);
            writer.WriteShort((short)(Cells == null ? 0 : Cells.Length));
            if (Cells != null) writer.WriteBytes(Cells, 0, Cells.Length);
        }

        public void Read(NetworkReader reader)
        {
            PosX = WireGuard.ReadCoordinate(reader);
            PosY = WireGuard.ReadCoordinate(reader);
            PosZ = WireGuard.ReadCoordinate(reader);
            int names = reader.ReadByte();
            ZoneNames = new string[names];
            for (int i = 0; i < names; i++) ZoneNames[i] = WireGuard.ReadName(reader);
            int cells = reader.ReadShort();
            if (cells < 0 || cells > WireGuard.MaxItemCount || cells > reader.Remaining)
                throw new ProtocolException("Implausible cell count: " + cells + ".");
            Cells = reader.ReadBytes(cells);
        }

        public byte[] Encode()
        {
            var writer = new NetworkWriter(128);
            Write(writer);
            return writer.ToArray();
        }

        public static ZonePaintCommand Decode(byte[] body)
        {
            var command = new ZonePaintCommand();
            command.Read(new NetworkReader(body));
            return command;
        }
    }
}
