using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;

namespace OpenRA.ReplayReader
{
    public static class OrderDecoder
    {
        // Convert hex string format "00-11-22-33" to byte array
        private static byte[] HexStringToByteArray(string hexData)
        {
            if (string.IsNullOrEmpty(hexData))
                return Array.Empty<byte>();

            return hexData.Split('-')
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => Convert.ToByte(s, 16))
                .ToArray();
        }

        // Main decoder method that dispatches to the appropriate specialized decoder
        public static string DecodeOrderExtraData(string orderType, string hexData)
        {
            if (string.IsNullOrEmpty(hexData))
                return "No extra data";

            // Convert hex string to byte array for easier handling
            var bytes = HexStringToByteArray(hexData);
            if (bytes.Length == 0)
                return "Invalid hex data";

            // First check specific order types
            switch (orderType)
            {
                // Production orders
                case "StartProduction":
                    return DecodeStartProductionExtraData(bytes);
                case "PauseProduction":
                    return DecodePauseProductionExtraData(bytes);
                case "CancelProduction":
                    return DecodeCancelProductionExtraData(bytes);
                
                // Building placement
                case "PlaceBuilding":
                    return DecodePlaceBuildingExtraData(bytes);
                
                // Movement and targeting orders
                case "Move":
                case "Attack":
                case "AttackMove":
                    return DecodeTargetPositionExtraData(bytes);
                
                // Support powers
                case "SetRallyPoint":
                    return DecodeSetRallyPointExtraData(bytes);
                case "Chronoshift":
                    return DecodeChronoshiftExtraData(bytes);
                case "IronCurtain":
                    return DecodeIronCurtainExtraData(bytes);
                case "NukePower":
                    return DecodeNukePowerExtraData(bytes);
                case "ParatroopersPower":
                    return DecodeParatroopersPowerExtraData(bytes);
                case "GpsPower":
                    return DecodeGpsPowerExtraData(bytes);
                case "AirstrikePower":
                    return DecodeAirstrikePowerExtraData(bytes);
                
                // Communication orders
                case "Chat":
                case "TeamChat":
                    return DecodeChatExtraData(bytes);
                
                // Management orders
                case "Sell":
                case "Repair":
                case "Deploy":
                case "DeployTransform":
                case "Undeploy":
                case "DeployToUpgrade":
                    return DecodeGenericFlagExtraData(bytes);
                
                // All other orders - use a generic decoder
                default:
                    return DecodeGenericExtraData(bytes);
            }
        }

        // Specific order type decoders

        public static string DecodeStartProductionExtraData(byte[] bytes)
        {
            if (bytes.Length < 4)
                return "Incomplete data: expected count value";

            // ExtraData is the count of items to produce
            var count = BitConverter.ToUInt32(bytes, 0);
            return $"Count: {count}";
        }

        public static string DecodePauseProductionExtraData(byte[] bytes)
        {
            if (bytes.Length < 4)
                return "Incomplete data: expected pause flag";

            // ExtraData is a flag indicating pause (1) or resume (0)
            var pauseFlag = BitConverter.ToUInt32(bytes, 0);
            return pauseFlag == 1 ? "Action: Pause" : "Action: Resume";
        }

        public static string DecodeCancelProductionExtraData(byte[] bytes)
        {
            if (bytes.Length < 4)
                return "Incomplete data: expected count value";

            // ExtraData is the count of items to cancel
            var count = BitConverter.ToUInt32(bytes, 0);
            return $"Count: {count}";
        }

        public static string DecodePlaceBuildingExtraData(byte[] bytes)
        {
            if (bytes.Length < 4)
                return "Incomplete data: expected placement data";

            // ExtraData may contain facing information or other placement data
            var placementData = BitConverter.ToUInt32(bytes, 0);
            
            // In many cases, the first byte is the facing direction (0-255)
            var facing = bytes[0];
            
            var sb = new StringBuilder();
            sb.AppendLine($"Raw Value: {placementData}");
            sb.AppendLine($"Facing Direction: {facing}");
            
            // Additional bytes may contain variant selection or other info
            if (bytes.Length > 4)
            {
                sb.AppendLine("Additional Data:");
                for (int i = 4; i < bytes.Length; i++)
                {
                    sb.AppendLine($"  Byte {i}: 0x{bytes[i]:X2}");
                }
            }
            
            return sb.ToString().TrimEnd();
        }

        public static string DecodeTargetPositionExtraData(byte[] bytes)
        {
            if (bytes.Length < 4)
                return "Incomplete data: expected position data";

            // For targeting orders, ExtraData might contain additional targeting information
            var data = BitConverter.ToUInt32(bytes, 0);
            
            var sb = new StringBuilder();
            sb.AppendLine($"Target Data: {data}");
            
            // For some orders, bytes may represent target coordinates or other data
            if (bytes.Length >= 4)
            {
                var x = BitConverter.ToInt16(bytes, 0);
                var y = BitConverter.ToInt16(bytes, 2);
                sb.AppendLine($"Coordinates: ({x}, {y})");
            }
            
            if (bytes.Length > 4)
            {
                sb.AppendLine("Additional Data:");
                for (int i = 4; i < bytes.Length; i++)
                {
                    sb.AppendLine($"  Byte {i}: 0x{bytes[i]:X2}");
                }
            }
            
            return sb.ToString().TrimEnd();
        }

        public static string DecodeSetRallyPointExtraData(byte[] bytes)
        {
            if (bytes.Length < 4)
                return "Incomplete data: expected cell position";

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

        public static string DecodeChronoshiftExtraData(byte[] bytes)
        {
            if (bytes.Length < 8)
                return "Incomplete data: expected source and destination data";

            var sb = new StringBuilder();
            
            if (bytes.Length >= 8)
            {
                // First 4 bytes may contain source cell info
                var sourceX = BitConverter.ToInt16(bytes, 0);
                var sourceY = BitConverter.ToInt16(bytes, 2);
                sb.AppendLine($"Source: ({sourceX}, {sourceY})");
                
                // Next 4 bytes may contain destination cell info
                var destX = BitConverter.ToInt16(bytes, 4);
                var destY = BitConverter.ToInt16(bytes, 6);
                sb.AppendLine($"Destination: ({destX}, {destY})");
            }
            
            if (bytes.Length > 8)
            {
                // Additional bytes may contain duration or other parameters
                var duration = BitConverter.ToUInt32(bytes, 8);
                sb.AppendLine($"Duration: {duration}");
                
                if (bytes.Length > 12)
                {
                    sb.AppendLine("Additional Data:");
                    for (int i = 12; i < bytes.Length; i++)
                    {
                        sb.AppendLine($"  Byte {i}: 0x{bytes[i]:X2}");
                    }
                }
            }
            
            return sb.ToString().TrimEnd();
        }

        public static string DecodeIronCurtainExtraData(byte[] bytes)
        {
            if (bytes.Length < 4)
                return "Incomplete data";

            var sb = new StringBuilder();
            
            // ExtraData may contain duration information
            var duration = BitConverter.ToUInt32(bytes, 0);
            sb.AppendLine($"Duration: {duration}");
            
            if (bytes.Length > 4)
            {
                sb.AppendLine("Additional Data:");
                for (int i = 4; i < bytes.Length; i++)
                {
                    sb.AppendLine($"  Byte {i}: 0x{bytes[i]:X2}");
                }
            }
            
            return sb.ToString().TrimEnd();
        }

        public static string DecodeNukePowerExtraData(byte[] bytes)
        {
            if (bytes.Length < 4)
                return "Incomplete data";

            var sb = new StringBuilder();
            
            // Some powers may use ExtraData for damage modifiers or special effects
            var data = BitConverter.ToUInt32(bytes, 0);
            sb.AppendLine($"Power Data: {data}");
            
            if (bytes.Length >= 4)
            {
                // Target coordinates often stored in first 4 bytes
                var x = BitConverter.ToInt16(bytes, 0);
                var y = BitConverter.ToInt16(bytes, 2);
                sb.AppendLine($"Target Cell: ({x}, {y})");
            }
            
            if (bytes.Length > 4)
            {
                sb.AppendLine("Additional Data:");
                for (int i = 4; i < bytes.Length; i++)
                {
                    sb.AppendLine($"  Byte {i}: 0x{bytes[i]:X2}");
                }
            }
            
            return sb.ToString().TrimEnd();
        }

        public static string DecodeParatroopersPowerExtraData(byte[] bytes)
        {
            return DecodeTargetPositionExtraData(bytes); // Often similar to target position orders
        }

        public static string DecodeGpsPowerExtraData(byte[] bytes)
        {
            if (bytes.Length < 4)
                return "Incomplete data";

            var sb = new StringBuilder();
            
            // Duration or reveal time
            var duration = BitConverter.ToUInt32(bytes, 0);
            sb.AppendLine($"Duration: {duration}");
            
            if (bytes.Length > 4)
            {
                sb.AppendLine("Additional Data:");
                for (int i = 4; i < bytes.Length; i++)
                {
                    sb.AppendLine($"  Byte {i}: 0x{bytes[i]:X2}");
                }
            }
            
            return sb.ToString().TrimEnd();
        }

        public static string DecodeAirstrikePowerExtraData(byte[] bytes)
        {
            return DecodeTargetPositionExtraData(bytes); // Often similar to target position orders
        }

        public static string DecodeChatExtraData(byte[] bytes)
        {
            if (bytes.Length < 4)
                return "Incomplete data";

            // For Chat orders, ExtraData contains the team number (0 = all chat, >0 = team chat)
            var teamNumber = BitConverter.ToUInt32(bytes, 0);
            return teamNumber == 0 ? "Channel: All Players" : $"Channel: Team {teamNumber}";
        }

        public static string DecodeGenericFlagExtraData(byte[] bytes)
        {
            if (bytes.Length < 4)
                return "Incomplete data";

            // Many orders use a simple flag to indicate on/off or other simple states
            var flag = BitConverter.ToUInt32(bytes, 0);
            return $"Flag Value: {flag}";
        }

        public static string DecodeGenericExtraData(byte[] bytes)
        {
            if (bytes.Length == 0)
                return "No data";

            var sb = new StringBuilder();
            
            // Handle common 4-byte data pattern
            if (bytes.Length >= 4)
            {
                var rawValue = BitConverter.ToUInt32(bytes, 0);
                sb.AppendLine($"Value: {rawValue} (0x{rawValue:X8})");
            }
            
            // Show all bytes in hex for debugging
            sb.AppendLine("Raw Data:");
            for (int i = 0; i < bytes.Length; i += 4)
            {
                var lineSb = new StringBuilder($"  {i:D4}: ");
                for (int j = 0; j < 4 && i + j < bytes.Length; j++)
                {
                    lineSb.Append($"{bytes[i + j]:X2} ");
                }
                sb.AppendLine(lineSb.ToString().TrimEnd());
            }
            
            // Try to interpret as coordinates if it's 4 or more bytes
            if (bytes.Length >= 4)
            {
                try
                {
                    var x = BitConverter.ToInt16(bytes, 0);
                    var y = BitConverter.ToInt16(bytes, 2);
                    sb.AppendLine($"As Coordinates: ({x}, {y})");
                }
                catch
                {
                    // Ignore interpretation errors
                }
            }
            
            return sb.ToString().TrimEnd();
        }
    }
}
