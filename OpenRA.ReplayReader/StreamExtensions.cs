using System;
using System.IO;
using System.Text;

namespace OpenRA.ReplayReader
{
    public static class StreamExtensions
    {
        public static int ReadInt32(this Stream s)
        {
            var b = new byte[4];
            s.Read(b, 0, 4);
            return BitConverter.ToInt32(b, 0);
        }
        
        public static uint ReadUInt32(this Stream s)
        {
            var b = new byte[4];
            s.Read(b, 0, 4);
            return BitConverter.ToUInt32(b, 0);
        }
        
        public static byte ReadUInt8(this Stream s)
        {
            return (byte)s.ReadByte();
        }
        
        public static string ReadLengthPrefixedString(this Stream s, Encoding encoding, int maxLength)
        {
            var length = s.ReadInt32();
            if (length > maxLength)
                throw new InvalidDataException($"String length {length} is longer than the maximum allowed length {maxLength}");
            
            var bytes = new byte[length];
            s.Read(bytes, 0, length);
            return encoding.GetString(bytes);
        }
        
        public static byte[] ReadBytes(this Stream s, int count)
        {
            var buffer = new byte[count];
            var read = s.Read(buffer, 0, count);
            if (read != count)
                throw new EndOfStreamException($"Requested {count} bytes but could only read {read}");
            return buffer;
        }
        
        public static void Write(this Stream s, int value)
        {
            var bytes = BitConverter.GetBytes(value);
            s.Write(bytes, 0, bytes.Length);
        }
        
        public static int WriteLengthPrefixedString(this Stream s, Encoding encoding, string value)
        {
            var bytes = encoding.GetBytes(value);
            s.Write(bytes.Length);
            s.Write(bytes, 0, bytes.Length);
            return 4 + bytes.Length;
        }
    }
}
