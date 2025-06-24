using System;

namespace OpenRA.ReplayReader
{
    // These enums are copied from the OpenRA codebase to make parsing replays possible
    // without having direct dependencies on the game code
    
    public enum OrderType : byte
    {
        Ack = 0x10,
        Ping = 0x20,
        SyncHash = 0x65,
        TickScale = 0x76,
        Disconnect = 0xBF,
        Handshake = 0xFE,
        Fields = 0xFF
    }

    [Flags]
    public enum OrderFields : short
    {
        None = 0x0,
        Target = 0x01,
        ExtraActors = 0x02,
        TargetString = 0x04,
        Queued = 0x08,
        ExtraLocation = 0x10,
        ExtraData = 0x20,
        TargetIsCell = 0x40,
        Subject = 0x80,
        Grouped = 0x100
    }
    
    public enum TargetType : byte
    {
        Invalid = 0,
        Actor = 1,
        FrozenActor = 2,
        Terrain = 3
    }
    
    public enum WinState : int
    {
        Undefined = 0,
        Won = 1,
        Lost = 2
    }
    
    public enum SubCell : byte
    {
        Invalid = 0,
        FullCell = 1,
        Any = 255
    }
}
