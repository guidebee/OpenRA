using System;
using System.IO;

namespace OpenRA.TemplateReader
{
    public static class TemplateStreamExts
    {
        public static byte ReadUInt8(this Stream s)
        {
            var b = s.ReadByte();
            if (b == -1)
                throw new EndOfStreamException();
            return (byte)b;
        }

        public static ushort ReadUInt16(this Stream s)
        {
            var b1 = s.ReadByte();
            var b2 = s.ReadByte();
            
            if (b1 == -1 || b2 == -1)
                throw new EndOfStreamException();
                
            return (ushort)(b1 | (b2 << 8));
        }
    }
}
