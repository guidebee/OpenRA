using System;
using System.IO;
using System.Text;
using System.Linq;

namespace OpenRA.ReplayReader
{
    public static class OrderDecoder
    {
        public static string DecodeSetRallyPointExtraData(string hexData)
        {
            if (string.IsNullOrEmpty(hexData))
                return "No extra data";

            // Remove dashes and convert to byte array
            var bytes = hexData.Split('-')
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => Convert.ToByte(s, 16))
                .ToArray();

            if (bytes.Length == 0)
                return "Invalid hex data";

            // For SetRallyPoint, the ExtraData typically contains:
            // - Target cell position (4 bytes)
            // - SubCell value (1 byte)
            // - Additional flags or data (remaining bytes)
            var sb = new StringBuilder();
            
            if (bytes.Length >= 4)
            {
                // First 2 bytes are X coordinate (little endian)
                var x = BitConverter.ToInt16(bytes, 0);
                // Next 2 bytes are Y coordinate (little endian)
                var y = BitConverter.ToInt16(bytes, 2);
                sb.AppendLine($"Target Cell: ({x}, {y})");
            }

            if (bytes.Length >= 5)
            {
                var subCell = bytes[4];
                sb.AppendLine($"SubCell: {subCell}");
            }

            if (bytes.Length > 5)
            {
                sb.AppendLine("Additional Data:");
                for (int i = 5; i < bytes.Length; i++)
                {
                    sb.AppendLine($"  Byte {i}: 0x{bytes[i]:X2}");
                }
            }

            return sb.ToString().TrimEnd();
        }

        public static string DecodeOrderExtraData(string orderType, string hexData)
        {
            if (string.IsNullOrEmpty(hexData))
                return "No extra data";

            switch (orderType)
            {
                case "SetRallyPoint":
                    return DecodeSetRallyPointExtraData(hexData);
                // Add more order type decoders as needed
                default:
                    return $"Raw hex data: {hexData}";
            }
        }
    }
}
