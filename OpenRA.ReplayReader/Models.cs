using System;
using System.Collections.Generic;

namespace OpenRA.ReplayReader
{
    public class ReplayData
    {
        public ReplayMetaData Metadata { get; set; }
        public List<OrderData> Orders { get; set; } = new();
    }

    public class ReplayMetaData
    {
        public string Mod { get; set; }
        public string Version { get; set; }
        public string MapUid { get; set; }
        public string MapTitle { get; set; }
        public DateTime StartTimeUtc { get; set; }
        public DateTime EndTimeUtc { get; set; }
        public int FinalGameTick { get; set; }
        public Dictionary<string, PlayerData> Players { get; set; }
    }

    public class PlayerData
    {
        public string Name { get; set; }
        public string Faction { get; set; }
        public string Team { get; set; }
        public bool IsHuman { get; set; }
        public bool IsBot { get; set; }
        public int ClientIndex { get; set; }
        public string Color { get; set; }
        public string Outcome { get; set; }
    }

    public class OrderData
    {
        public int Frame { get; set; }
        public int ClientId { get; set; }
        public OrderType Type { get; set; }
        public string OrderString { get; set; }
        public OrderTarget Target { get; set; }
        public string TargetString { get; set; }
        public bool IsQueued { get; set; }
        public uint ExtraData { get; set; }
        public CPos ExtraLocation { get; set; }
        public bool IsImmediate { get; set; }
    }

    public class OrderTarget
    {
        public TargetType Type { get; set; }
        public uint? ActorId { get; set; }
        public int? ActorGeneration { get; set; }
        public uint? FrozenActorId { get; set; }
        public CPos? Cell { get; set; }
        public SubCell? SubCell { get; set; }
        public WPos? Position { get; set; }
        public WPos[] TerrainPositions { get; set; }
    }

    public class CPos
    {
        public int X { get; set; }
        public int Y { get; set; }

        public CPos(int x, int y)
        {
            X = x;
            Y = y;
        }

        public static CPos Zero => new CPos(0, 0);
    }

    public class WPos
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }

        public WPos(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }
}
