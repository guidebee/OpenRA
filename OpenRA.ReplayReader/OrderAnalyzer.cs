using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace OpenRA.ReplayReader
{
    public class OrderInfo
    {
        public int Frame { get; set; }
        public int ClientId { get; set; }
        public string OrderType { get; set; }
        public string TargetString { get; set; }
        public bool HasSubject { get; set; }
        public bool HasTarget { get; set; }
        public bool IsQueued { get; set; }
        public int? SubjectActorId { get; set; }
        public string ExtraData { get; set; }
    }

    public class OrderAnalyzer
    {
        // Complete set of OpenRA order types for reference, expanded from the Analysis tool
        public static readonly HashSet<string> MovementOrders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Move", "Stop", "Scatter", "ForceMove", "ForceMoveIntoTarget", "Infiltrate",
            "Enter", "EnterTransport", "Exit", "Unload", "Dock", "ReturnToBase",
            "AttackMove", "AssaultMove", "Patrol"
        };

        public static readonly HashSet<string> CombatOrders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Attack", "AttackMove", "ForceAttack", "Guard", "AssaultMove", "Detonate", "DetonateAttack",
            "C4", "Demolish", "Explode", "TargetPoint", "TargetLineMove"
        };

        public static readonly HashSet<string> ProductionOrders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "StartProduction", "PauseProduction", "CancelProduction", "BuildBuilding", "PlaceBuilding", 
            "DeployTransform", "Undeploy", "PlaceObject", "DeployMcv", "Production", "Deploy",
            "GrantUpgrade", "Produce", "BuildArms", "BuildNaval", "BuildVehicle", "BuildAircraft",
            "BuildBuilding", "TrainInfantry", "SetPrimaryBuilding", "QueueUnit", "StartConstruction", 
            "ToggleProduction"
        };

        public static readonly HashSet<string> SupportOrders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Power", "Repair", "Sell", "Capture", "Heal", "SetRallyPoint", "Harvest", 
            "ReturnToRefinery", "DeliverCash", "DeliverExperience", "ToggleProduction",
            "Chronoshift", "IronCurtain", "GpsPower", "ParatroopersPower", "NukePower",
            "Sonar", "SpyPlane", "Airstrike", "AdvancedChronoshift", "GrantExternalCondition",
            "RepairBridge", "Steal", "Infiltrate", "Disguise", "Demolish", "DeployTransform",
            "DeployToUpgrade", "Chronosphere", "IronCurtain", "NukePowerInfoOrder", "DropSpecialPower"
        };

        // Order categorization
        public static string CategorizeOrder(string orderType)
        {
            if (MovementOrders.Contains(orderType))
                return "Movement";
            if (CombatOrders.Contains(orderType))
                return "Combat";
            if (ProductionOrders.Contains(orderType))
                return "Production";
            if (SupportOrders.Contains(orderType))
                return "Support";
            
            // Special cases
            if (orderType == "Chat" || orderType == "TeamChat")
                return "Communication";
            if (orderType == "SyncHash" || orderType == "Disconnect" || orderType == "Ping" 
                || orderType == "Handshake" || orderType == "StateHash")
                return "Network";
            
            return "Other";
        }
        
        // Parse a replay file directly without relying on metadata
        public static List<OrderInfo> ParseReplayOrders(string replayPath)
        {
            var orders = new List<OrderInfo>();
            
            try
            {
                using (var fs = File.OpenRead(replayPath))
                {
                    long position = 0;
                    while (position < fs.Length - 8)  // Need at least clientId (4) and length (4)
                    {
                        fs.Position = position;
                        
                        // Read client ID and packet length
                        var clientIdBytes = new byte[4];
                        fs.Read(clientIdBytes, 0, 4);
                        var clientId = BitConverter.ToInt32(clientIdBytes, 0);
                        
                        // Check if we've reached metadata or end of replay data
                        if (clientId == ReplayMetadata.MetaStartMarker || clientId == ReplayMetadata.MetaEndMarker)
                            break;
                        
                        // Sanity check for client IDs - must be a reasonable value
                        if (clientId < -2 || clientId > 32)  
                        {
                            position++;
                            continue;
                        }
                        
                        var packetLenBytes = new byte[4];
                        fs.Read(packetLenBytes, 0, 4);
                        var packetLen = BitConverter.ToInt32(packetLenBytes, 0);
                        
                        // Sanity check for packet length
                        if (packetLen <= 0 || packetLen > 100000 || position + 8 + packetLen > fs.Length)  
                        {
                            position++;
                            continue;
                        }
                        
                        // Read the packet data
                        var packetData = new byte[packetLen];
                        fs.Read(packetData, 0, packetLen);
                        
                        // Attempt to parse the order from the packet
                        if (packetLen >= 5)  // Minimum size for an order packet (frame + ordertype)
                        {
                            var frameBytes = new byte[4];
                            Array.Copy(packetData, 0, frameBytes, 0, 4);
                            var frame = BitConverter.ToInt32(frameBytes, 0);
                            
                            var orderType = (OrderType)packetData[4];
                            
                            // Skip non-order packets
                            if (orderType == OrderType.SyncHash || orderType == OrderType.Disconnect)
                            {
                                orders.Add(new OrderInfo
                                {
                                    Frame = frame,
                                    ClientId = clientId,
                                    OrderType = orderType.ToString()
                                });
                            }
                            else if (orderType == OrderType.Fields && packetLen > 5)
                            {
                                try
                                {
                                    using (var ms = new MemoryStream(packetData, 5, packetLen - 5))
                                    using (var reader = new BinaryReader(ms))
                                    {
                                        var orderString = reader.ReadString();
                                        var orderFlags = (OrderFields)reader.ReadInt16();
                                        
                                        var orderInfo = new OrderInfo
                                        {
                                            Frame = frame,
                                            ClientId = clientId,
                                            OrderType = orderString,
                                            HasSubject = ((int)orderFlags & (int)OrderFields.Subject) != 0,
                                            HasTarget = ((int)orderFlags & (int)OrderFields.Target) != 0,
                                            IsQueued = ((int)orderFlags & (int)OrderFields.Queued) != 0
                                        };
                                        
                                        // Read subject actor ID if present
                                        if (orderInfo.HasSubject && ms.Position < ms.Length)
                                        {
                                            orderInfo.SubjectActorId = reader.ReadInt32();
                                        }
                                        
                                        // Read target string if present
                                        if (((int)orderFlags & (int)OrderFields.TargetString) != 0 && ms.Position < ms.Length)
                                        {
                                            orderInfo.TargetString = reader.ReadString();
                                        }
                                        
                                        // Attempt to read any extra data (simplified)
                                        if (ms.Position < ms.Length)
                                        {
                                            var remainingBytes = new byte[ms.Length - ms.Position];
                                            ms.Read(remainingBytes, 0, remainingBytes.Length);
                                            orderInfo.ExtraData = BitConverter.ToString(remainingBytes);
                                        }
                                        
                                        orders.Add(orderInfo);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Error parsing order at position {position}: {ex.Message}");
                                }
                            }
                            else
                            {
                                // Unknown order type
                                orders.Add(new OrderInfo
                                {
                                    Frame = frame,
                                    ClientId = clientId,
                                    OrderType = $"Unknown_{orderType}"
                                });
                            }
                        }
                        
                        // Move to next packet
                        position += 8 + packetLen;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing replay file: {ex.Message}");
            }
            
            return orders;
        }
        
        // Methods to generate timeline statistics
        public static Dictionary<int, Dictionary<string, int>> GenerateOrderTimelineByCategory(
            List<OrderInfo> orders, int bucketSize = 100)
        {
            if (orders.Count == 0)
                return new Dictionary<int, Dictionary<string, int>>();
            
            var ordersByFrame = orders.GroupBy(o => o.Frame)
                .ToDictionary(g => g.Key, g => g.ToList());
            
            var result = new Dictionary<int, Dictionary<string, int>>();
            var maxFrame = orders.Max(o => o.Frame);
            
            for (int frame = 0; frame <= maxFrame; frame += bucketSize)
            {
                var categories = new Dictionary<string, int>
                {
                    { "Movement", 0 },
                    { "Combat", 0 },
                    { "Production", 0 },
                    { "Support", 0 },
                    { "Communication", 0 },
                    { "Network", 0 },
                    { "Other", 0 }
                };
                
                // Aggregate orders for this time bucket
                for (int f = frame; f < frame + bucketSize && f <= maxFrame; f++)
                {
                    if (ordersByFrame.TryGetValue(f, out var frameOrders))
                    {
                        foreach (var order in frameOrders)
                        {
                            var category = CategorizeOrder(order.OrderType);
                            categories[category]++;
                        }
                    }
                }
                
                result[frame] = categories;
            }
            
            return result;
        }
        
        public static Dictionary<int, Dictionary<int, int>> GenerateOrderTimelineByPlayer(
            List<OrderInfo> orders, int bucketSize = 100)
        {
            if (orders.Count == 0)
                return new Dictionary<int, Dictionary<int, int>>();
            
            var ordersByFrame = orders.GroupBy(o => o.Frame)
                .ToDictionary(g => g.Key, g => g.ToList());
            
            var result = new Dictionary<int, Dictionary<int, int>>();
            var maxFrame = orders.Max(o => o.Frame);
            var players = orders.Select(o => o.ClientId).Distinct().ToList();
            
            for (int frame = 0; frame <= maxFrame; frame += bucketSize)
            {
                var playerCounts = new Dictionary<int, int>();
                foreach (var player in players)
                    playerCounts[player] = 0;
                
                // Aggregate orders for this time bucket
                for (int f = frame; f < frame + bucketSize && f <= maxFrame; f++)
                {
                    if (ordersByFrame.TryGetValue(f, out var frameOrders))
                    {
                        foreach (var order in frameOrders)
                        {
                            if (playerCounts.ContainsKey(order.ClientId))
                                playerCounts[order.ClientId]++;
                            else
                                playerCounts[order.ClientId] = 1;
                        }
                    }
                }
                
                result[frame] = playerCounts;
            }
            
            return result;
        }
        
        // Methods to print timeline statistics in ASCII format
        public static void PrintOrderTimelineVisualization(Dictionary<int, Dictionary<string, int>> timelineBuckets, int maxWidth = 80)
        {
            if (timelineBuckets.Count == 0)
                return;
            
            Console.WriteLine("\nOrder Distribution Timeline (by category):");
            Console.WriteLine("============================================");
            
            // Find the maximum count to normalize the chart
            var maxCount = timelineBuckets.Values.Max(dict => dict.Values.Sum());
            
            foreach (var kvp in timelineBuckets.OrderBy(x => x.Key))
            {
                var frame = kvp.Key;
                var categories = kvp.Value;
                var total = categories.Values.Sum();
                
                // Frame info
                var frameInfo = $"Frame {frame,8}: ";
                var seconds = frame / 60.0; // Assuming 60 FPS
                Console.Write($"{frameInfo}[{seconds,6:F1}s] ");
                
                // Print normalized bar
                var barWidth = maxWidth - frameInfo.Length - 20; // Allow space for text
                var normalizedTotal = total > 0 
                    ? Math.Min(barWidth, (int)Math.Round(barWidth * ((double)total / maxCount)))
                    : 0;
                
                if (normalizedTotal > 0)
                {
                    // Calculate proportions for each category
                    var movementWidth = total > 0 ? (int)Math.Round(normalizedTotal * ((double)categories["Movement"] / total)) : 0;
                    var combatWidth = total > 0 ? (int)Math.Round(normalizedTotal * ((double)categories["Combat"] / total)) : 0;
                    var productionWidth = total > 0 ? (int)Math.Round(normalizedTotal * ((double)categories["Production"] / total)) : 0;
                    var supportWidth = total > 0 ? (int)Math.Round(normalizedTotal * ((double)categories["Support"] / total)) : 0;
                    var otherWidth = normalizedTotal - movementWidth - combatWidth - productionWidth - supportWidth;
                    
                    // Print each category with different characters
                    Console.Write(new string('M', movementWidth));   // Movement
                    Console.Write(new string('C', combatWidth));     // Combat
                    Console.Write(new string('P', productionWidth)); // Production
                    Console.Write(new string('S', supportWidth));    // Support
                    Console.Write(new string('O', otherWidth));      // Other
                }
                
                Console.WriteLine($" ({total} orders)");
            }
            
            // Print legend
            Console.WriteLine("\nLegend: M = Movement, C = Combat, P = Production, S = Support, O = Other");
        }
        
        public static void PrintPlayerActivityVisualization(
            Dictionary<int, Dictionary<int, int>> timelineByPlayer, 
            Dictionary<int, string> playerNames,
            int maxWidth = 80)
        {
            if (timelineByPlayer.Count == 0)
                return;
            
            Console.WriteLine("\nPlayer Activity Timeline:");
            Console.WriteLine("=========================");
            
            // Find all players and the maximum count
            var players = timelineByPlayer.Values
                .SelectMany(dict => dict.Keys)
                .Distinct()
                .OrderBy(p => p)
                .ToList();
            
            var maxCount = timelineByPlayer.Values.Max(dict => dict.Values.Sum());
            
            // Print header with player IDs/names
            Console.Write("Frame      |");
            foreach (var player in players)
            {
                var name = playerNames.TryGetValue(player, out var pName) ? pName : $"Player {player}";
                Console.Write($" {name,-10}|");
            }
            Console.WriteLine();
            
            // Print a separator line
            Console.WriteLine(new string('-', 12 + (players.Count * 12)));
            
            // Print each time bucket
            foreach (var kvp in timelineByPlayer.OrderBy(x => x.Key))
            {
                var frame = kvp.Key;
                var playerCounts = kvp.Value;
                
                // Frame info
                var seconds = frame / 60.0; // Assuming 60 FPS
                Console.Write($"{frame,6} ({seconds,4:F1}s)|");
                
                // Print each player's activity
                foreach (var player in players)
                {
                    var count = playerCounts.TryGetValue(player, out var c) ? c : 0;
                    
                    // Normalize to a 0-10 scale for visualization
                    var normalizedCount = maxCount > 0 
                        ? Math.Min(10, (int)Math.Round(10.0 * count / maxCount))
                        : 0;
                    
                    var bar = normalizedCount > 0 
                        ? new string('#', normalizedCount) + new string(' ', 10 - normalizedCount)
                        : new string(' ', 10);
                    
                    Console.Write($" {bar}|");
                }
                Console.WriteLine();
            }
        }
        
        // Analyze orders and print common statistics
        public static void AnalyzeAndPrintOrderStatistics(List<OrderInfo> orders)
        {
            if (orders.Count == 0)
            {
                Console.WriteLine("No orders found to analyze.");
                return;
            }
            
            Console.WriteLine($"\nTotal orders: {orders.Count}");
            
            // Order type statistics
            var orderTypes = orders
                .GroupBy(o => o.OrderType)
                .Select(g => new { Type = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ToList();
            
            Console.WriteLine("\nOrder Type Statistics:");
            foreach (var type in orderTypes.Take(15)) // Show top 15
            {
                Console.WriteLine($"  {type.Type}: {type.Count} orders ({(double)type.Count / orders.Count:P1})");
            }
            
            // Order category statistics
            var orderCategories = orders
                .GroupBy(o => CategorizeOrder(o.OrderType))
                .Select(g => new { Category = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ToList();
            
            Console.WriteLine("\nOrder Category Statistics:");
            foreach (var category in orderCategories)
            {
                Console.WriteLine($"  {category.Category}: {category.Count} orders ({(double)category.Count / orders.Count:P1})");
            }
            
            // Player statistics
            var playerOrders = orders
                .GroupBy(o => o.ClientId)
                .Select(g => new { ClientId = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ToList();
            
            Console.WriteLine("\nPlayer Activity Statistics:");
            foreach (var player in playerOrders)
            {
                Console.WriteLine($"  Player {player.ClientId}: {player.Count} orders ({(double)player.Count / orders.Count:P1})");
            }
            
            // Frame range
            var minFrame = orders.Min(o => o.Frame);
            var maxFrame = orders.Max(o => o.Frame);
            var duration = maxFrame / 60.0; // Assuming 60 FPS
            
            Console.WriteLine($"\nFrame Range: {minFrame} to {maxFrame} (Duration: {duration:F1} seconds)");
            Console.WriteLine($"Average Orders Per Second: {orders.Count / Math.Max(1, duration):F1}");
            
            // Print a sample of orders from different parts of the replay
            Console.WriteLine("\nSample Orders:");
            
            // Start of game
            var startOrders = orders
                .Where(o => o.Frame <= minFrame + 100)
                .OrderBy(o => o.Frame)
                .Take(5)
                .ToList();
            
            Console.WriteLine("  Start of Game:");
            foreach (var order in startOrders)
            {
                PrintOrderDetails(order, "    ");
            }
            
            // Middle of game
            var midPoint = (minFrame + maxFrame) / 2;
            var midOrders = orders
                .Where(o => Math.Abs(o.Frame - midPoint) <= 50)
                .OrderBy(o => o.Frame)
                .Take(5)
                .ToList();
            
            Console.WriteLine("  Middle of Game:");
            foreach (var order in midOrders)
            {
                PrintOrderDetails(order, "    ");
            }
            
            // End of game
            var endOrders = orders
                .Where(o => o.Frame >= maxFrame - 100)
                .OrderByDescending(o => o.Frame)
                .Take(5)
                .ToList();
            
            Console.WriteLine("  End of Game:");
            foreach (var order in endOrders)
            {
                PrintOrderDetails(order, "    ");
            }
        }
        
        private static void PrintOrderDetails(OrderInfo order, string indent = "")
        {
            var time = order.Frame / 60.0; // Assuming 60 FPS
            Console.Write($"{indent}Frame {order.Frame} ({time:F1}s), Client {order.ClientId}, Order: {order.OrderType}");
            
            if (order.HasSubject)
                Console.Write($", Subject: {order.SubjectActorId}");
            
            if (order.HasTarget)
                Console.Write(", Has Target");
            
            if (order.IsQueued)
                Console.Write(", Queued");
            
            if (!string.IsNullOrEmpty(order.TargetString))
                Console.Write($", Target: \"{order.TargetString}\"");
            
            Console.WriteLine();
        }
    }
}
