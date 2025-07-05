using System;
using System.IO;

namespace OpenRA.TilesetReader
{
    /// <summary>
    /// Extensions for reading binary data from streams
    /// </summary>
    public static class StreamExtensions
    {
        /// <summary>
        /// Reads an unsigned byte from the stream
        /// </summary>
        public static byte ReadUInt8(this Stream stream)
        {
            var result = stream.ReadByte();
            if (result == -1)
                throw new EndOfStreamException();
            return (byte)result;
        }

        /// <summary>
        /// Reads an unsigned 16-bit integer from the stream in little-endian format
        /// </summary>
        public static ushort ReadUInt16(this Stream stream)
        {
            var b1 = stream.ReadByte();
            var b2 = stream.ReadByte();
            
            if (b1 == -1 || b2 == -1)
                throw new EndOfStreamException();
            
            return (ushort)(b1 | (b2 << 8));
        }

        /// <summary>
        /// Reads an unsigned 32-bit integer from the stream in little-endian format
        /// </summary>
        public static uint ReadUInt32(this Stream stream)
        {
            var b1 = stream.ReadByte();
            var b2 = stream.ReadByte();
            var b3 = stream.ReadByte();
            var b4 = stream.ReadByte();
            
            if (b1 == -1 || b2 == -1 || b3 == -1 || b4 == -1)
                throw new EndOfStreamException();
            
            return (uint)(b1 | (b2 << 8) | (b3 << 16) | (b4 << 24));
        }
    }
}
