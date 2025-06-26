using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OpenRA.ReplayReader
{
    public class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("OpenRA Replay Reader");
            Console.WriteLine("====================");
            
            string replayPath;
            
            if (args.Length == 0)
            {
                Console.WriteLine("No replay file specified. Using default test replay file.");
                replayPath = "ra-2025-06-21T035849Z.orarep";
                
                if (!File.Exists(replayPath))
                {
                    Console.WriteLine($"Error: Default test file '{replayPath}' does not exist.");
                    Console.WriteLine("Please provide a path to an OpenRA replay file (.orarep)");
                    Console.WriteLine("Usage: OpenRA.ReplayReader <replay-file-path>");
                    return;
                }
            }
            else
            {
                replayPath = args[0];
                if (!File.Exists(replayPath))
                {
                    Console.WriteLine($"Error: File '{replayPath}' does not exist.");
                    return;
                }
            }
            
            if (!replayPath.EndsWith(".orarep", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Warning: File does not have .orarep extension. It might not be a valid OpenRA replay.");
            }
            
            try
            {
                AnalyzeReplayFile(replayPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error analyzing replay: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
            
            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }
        
        static void SaveOrdersToJson(string replayPath, List<OrderInfo> orders)
        {
            var jsonFilePath = Path.ChangeExtension(replayPath, ".json");

            try
            {
                var json = JsonSerializer.Serialize(orders, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(jsonFilePath, json);
                Console.WriteLine($"Orders saved to JSON file: {jsonFilePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving orders to JSON: {ex.Message}");
            }
        }

        static void AnalyzeReplayFile(string replayPath)
        {
            Console.WriteLine($"Analyzing replay: {replayPath}");
            Console.WriteLine();
            
            // Check if the file can be opened and read
            Console.WriteLine("Basic file diagnostics:");
            try
            {
                var fileInfo = new FileInfo(replayPath);
                Console.WriteLine($"  File size: {fileInfo.Length} bytes");
                Console.WriteLine($"  Last modified: {fileInfo.LastWriteTime}");
                
                // Try to read the first few bytes
                using (var fs = File.OpenRead(replayPath))
                {
                    var headerBytes = new byte[Math.Min(20, (int)fs.Length)];
                    fs.Read(headerBytes, 0, headerBytes.Length);
                    
                    Console.WriteLine("  First bytes (hex):");
                    Console.Write("    ");
                    for (int i = 0; i < headerBytes.Length; i++)
                        Console.Write($"{headerBytes[i]:X2} ");
                    Console.WriteLine();
                    
                    // Try to read the last few bytes
                    if (fs.Length > 20)
                    {
                        fs.Seek(-20, SeekOrigin.End);
                        var footerBytes = new byte[20];
                        fs.Read(footerBytes, 0, 20);
                        
                        Console.WriteLine("  Last bytes (hex):");
                        Console.Write("    ");
                        for (int i = 0; i < footerBytes.Length; i++)
                            Console.Write($"{footerBytes[i]:X2} ");
                        Console.WriteLine();
                        
                        // Check for metadata marker (-2 as int32)
                        var hasMetadataMarker = false;
                        for (int i = 0; i < footerBytes.Length - 3; i++)
                        {
                            if (footerBytes[i] == 0xFE && footerBytes[i + 1] == 0xFF && 
                                footerBytes[i + 2] == 0xFF && footerBytes[i + 3] == 0xFF)
                            {
                                hasMetadataMarker = true;
                                Console.WriteLine($"  Found potential metadata end marker at offset {i} from end");
                                break;
                            }
                        }
                        
                        if (!hasMetadataMarker)
                            Console.WriteLine("  Warning: Could not find metadata end marker in last 20 bytes");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Error accessing file: {ex.Message}");
            }
            
            // First try to read metadata for structured information
            Console.WriteLine("\nAttempting to read replay metadata...");
            ReplayMetadata metadata = null;
            
            try
            {
                metadata = ReplayMetadata.Read(replayPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading metadata: {ex.Message}");
            }
            
            if (metadata != null)
            {
                Console.WriteLine("Successfully read replay metadata!");
                DisplayReplayInfo(metadata);
            }
            else
            {
                Console.WriteLine("Could not read replay metadata. Attempting direct order analysis...");
            }
            
            // Always analyze orders using our improved direct parser since it works with or without metadata
            Console.WriteLine("\nAnalyzing orders in replay file...");
            var orders = OrderAnalyzer.ParseReplayOrders(replayPath);
            
            if (orders.Count > 0)
            {
                Console.WriteLine($"Successfully extracted {orders.Count} orders from the replay file.");
                
                // Save orders to JSON
                SaveOrdersToJson(replayPath, orders);

                // Create player name dictionary for visualizations
                var playerNames = new Dictionary<int, string>();
                
                // If metadata is available, use player names from it
                if (metadata != null && metadata.GameInfo.Players.Length > 0)
                {
                    int clientId = 0;
                    foreach (var player in metadata.GameInfo.Players)
                    {
                        var name = metadata.GameInfo.ResolvedPlayerName(player);
                        if (!string.IsNullOrEmpty(name))
                        {
                            playerNames[clientId] = name;
                            clientId++;
                        }
                    }
                }
                else
                {
                    // If no metadata, use default player names
                    var playerIds = orders
                        .Select(o => o.ClientId)
                        .Distinct()
                        .OrderBy(id => id)
                        .ToList();
                    
                    foreach (var id in playerIds)
                    {
                        playerNames[id] = $"Player {id}";
                    }
                }
                
                // Fix for categorization of AttackMove orders for visualization
                Console.WriteLine("\n=== Combat Order Detection Fix ===");
                var originalAttackMoveCount = orders.Count(o => o.OrderType.Equals("AttackMove", StringComparison.OrdinalIgnoreCase));
                var attackMoveFixedOrders = new List<OrderInfo>(orders);
                
                // Count before we fix anything
                var categorizedAsCombatBefore = attackMoveFixedOrders.Count(o => 
                    o.OrderType.Equals("AttackMove", StringComparison.OrdinalIgnoreCase) && 
                    OrderAnalyzer.CategorizeOrder(o.OrderType) == "Combat");
                
                // Create a custom categorizer for visualization only
                var combatPrioritizedOrders = new List<OrderInfo>();
                foreach (var order in orders)
                {
                    var newOrder = new OrderInfo
                    {
                        Frame = order.Frame,
                        ClientId = order.ClientId,
                        OrderType = order.OrderType,
                        HasSubject = order.HasSubject,
                        HasTarget = order.HasTarget,
                        IsQueued = order.IsQueued,
                        SubjectActorId = order.SubjectActorId,
                        TargetString = order.TargetString,
                        ExtraData = order.ExtraData
                    };
                    
                    // Special handling: Force AttackMove to be Combat
                    if (order.OrderType.Equals("AttackMove", StringComparison.OrdinalIgnoreCase))
                    {
                        // Add a special marker to make sure these are categorized as combat
                        newOrder.OrderType = "Combat_AttackMove";
                    }
                    
                    combatPrioritizedOrders.Add(newOrder);
                }
                
                Console.WriteLine($"Original AttackMove orders: {originalAttackMoveCount}");
                Console.WriteLine($"Categorized as Combat before fix: {categorizedAsCombatBefore}");
                Console.WriteLine($"Fixed for visualization: {combatPrioritizedOrders.Count(o => o.OrderType == "Combat_AttackMove")}");
                
                // Generate filtered timeline with game actions only (excluding network orders)
                var gameplayOrders = combatPrioritizedOrders.Where(o => 
                    !OrderAnalyzer.NetworkOrders.Contains(o.OrderType) &&
                    !o.OrderType.StartsWith("Unknown_")).ToList();
                
                // Analyze and display full order statistics
                OrderAnalyzer.AnalyzeAndPrintOrderStatistics(orders);
                
                // Generate timelines and visualizations
                var orderTimeline = OrderAnalyzer.GenerateOrderTimelineByCategory(orders, 30);
                
                // Skip empty frames at the beginning
                var firstNonEmptyFrame = orderTimeline
                    .Where(kvp => kvp.Value.Values.Sum() > 0)
                    .Select(kvp => kvp.Key)
                    .DefaultIfEmpty(0)
                    .Min();
                
                Console.WriteLine($"\n=== Full Timeline (including network orders, starting from frame {firstNonEmptyFrame}) ===");
                OrderAnalyzer.PrintOrderTimelineVisualization(
                    orderTimeline.Where(kvp => kvp.Key >= firstNonEmptyFrame).ToDictionary(kvp => kvp.Key, kvp => kvp.Value));
                
                // Filter out network orders for a clearer view of gameplay
                if (gameplayOrders.Any())
                {
                    Console.WriteLine("\n=== Gameplay Timeline (excluding network orders) ===");
                    var filteredTimeline = OrderAnalyzer.GenerateGameplayTimelineByCategory(gameplayOrders, 30);
                    OrderAnalyzer.PrintGameplayTimelineVisualization(filteredTimeline);
                    
                    // Print a special fixed version for combat orders
                    Console.WriteLine("\n=== Combat Orders Timeline ===");
                    var combatOnlyOrders = gameplayOrders.Where(o => 
                        o.OrderType == "Combat_AttackMove" || 
                        OrderAnalyzer.CombatOrders.Contains(o.OrderType))
                        .ToList();
                    
                    if (combatOnlyOrders.Any())
                    {
                        var combatTimeline = OrderAnalyzer.GenerateSpecializedTimeline(combatOnlyOrders, 30);
                        OrderAnalyzer.PrintSpecializedTimelineVisualization(combatTimeline, "Combat Orders");
                    }
                    else
                    {
                        Console.WriteLine("No combat orders found to display.");
                    }
                }
                
                // Generate player activity timeline
                var playerTimeline = OrderAnalyzer.GenerateOrderTimelineByPlayer(orders, 30);
                OrderAnalyzer.PrintPlayerActivityVisualization(playerTimeline, playerNames);
                
                // Analyze a specific area of interest if many combat orders are present
                var combatOrders = orders.Where(o => OrderAnalyzer.CombatOrders.Contains(o.OrderType)).ToList();
                if (combatOrders.Count > 5) 
                {
                    // Find peak combat frame
                    var peakCombatFrame = combatOrders
                        .GroupBy(o => o.Frame / 300)
                        .OrderByDescending(g => g.Count())
                        .FirstOrDefault()?.Key * 300 ?? 0;
                    
                    if (peakCombatFrame > 0)
                    {
                        Console.WriteLine($"\n=== Combat Activity Detail (around frame {peakCombatFrame}) ===");
                        var combatTimelineRange = orders
                            .Where(o => o.Frame >= peakCombatFrame - 300 && o.Frame <= peakCombatFrame + 300)
                            .ToList();
                        
                        var combatTimeline = OrderAnalyzer.GenerateOrderTimelineByCategory(combatTimelineRange, 30);
                        OrderAnalyzer.PrintOrderTimelineVisualization(combatTimeline);
                    }
                }
            }
            else
            {
                Console.WriteLine("No valid orders found in the replay file.");
                AnalyzeReplayPackets(replayPath);
            }
        }
        
        static void AnalyzeReplayPackets(string replayPath)
        {
            Console.WriteLine("\nFallback packet structure analysis:");
            int packetCount = 0;
            int clientIdMin = int.MaxValue;
            int clientIdMax = int.MinValue;
            
            try
            {
                using (var fs = File.OpenRead(replayPath))
                {
                    // Try to read raw client packets without relying on metadata
                    long position = 0;
                    while (position < fs.Length - 8)  // Need at least clientId (4) and length (4)
                    {
                        fs.Position = position;
                        
                        // Read client ID and packet length
                        var clientIdBytes = new byte[4];
                        fs.Read(clientIdBytes, 0, 4);
                        var clientId = BitConverter.ToInt32(clientIdBytes, 0);
                        
                        // Sanity check for client IDs
                        if (clientId < -10 || clientId > 100)  
                        {
                            position++;
                            continue;
                        }
                        
                        var packetLenBytes = new byte[4];
                        fs.Read(packetLenBytes, 0, 4);
                        var packetLen = BitConverter.ToInt32(packetLenBytes, 0);
                        
                        // Sanity check for packet length
                        if (packetLen < 0 || packetLen > 1000000 || position + 8 + packetLen > fs.Length)  
                        {
                            position++;
                            continue;
                        }
                        
                        // This looks like a valid packet
                        packetCount++;
                        clientIdMin = Math.Min(clientIdMin, clientId);
                        clientIdMax = Math.Max(clientIdMax, clientId);
                        
                        if (packetCount <= 5 || fs.Length - position < 1000)
                        {
                            // Print details of first few packets and last few packets
                            Console.WriteLine($"  Packet at position {position}:");
                            Console.WriteLine($"    Client ID: {clientId}");
                            Console.WriteLine($"    Length: {packetLen} bytes");
                            
                            if (packetLen >= 4)
                            {
                                var frameBytes = new byte[4];
                                fs.Read(frameBytes, 0, 4);
                                var frame = BitConverter.ToInt32(frameBytes, 0);
                                Console.WriteLine($"    Frame: {frame}");
                                
                                // Try to peek at the order type if available
                                if (packetLen > 4)
                                {
                                    var orderTypeByte = fs.ReadByte();
                                    var orderType = (OrderType)orderTypeByte;
                                    Console.WriteLine($"    Order Type: {orderType} (0x{orderTypeByte:X2})");
                                    
                                    // For Fields orders, try to read the order string
                                    if (orderType == OrderType.Fields && packetLen > 5)
                                    {
                                        try
                                        {
                                            // Go back to just after the order type
                                            fs.Position = position + 8 + 4 + 1;
                                            
                                            // Read order string length (simple .NET string prefix)
                                            var stringLenByte = fs.ReadByte();
                                            if (stringLenByte > 0 && stringLenByte < 128)
                                            {
                                                byte[] orderStringBytes = new byte[stringLenByte];
                                                fs.Read(orderStringBytes, 0, stringLenByte);
                                                string orderString = System.Text.Encoding.UTF8.GetString(orderStringBytes);
                                                Console.WriteLine($"    Order String: {orderString}");
                                            }
                                        }
                                        catch
                                        {
                                            // Ignore errors in trying to read order string
                                        }
                                    }
                                }
                            }
                        }
                        
                        // Skip to next packet
                        position += 8 + packetLen;
                    }
                }
                
                Console.WriteLine($"\nFound approximately {packetCount} packets");
                if (packetCount > 0)
                {
                    Console.WriteLine($"Client ID range: {clientIdMin} to {clientIdMax}");
                    
                    if (clientIdMin == ReplayMetadata.MetaStartMarker || clientIdMax == ReplayMetadata.MetaEndMarker)
                    {
                        Console.WriteLine("Warning: Found metadata markers among client IDs - this might indicate file corruption or different format version.");
                    }
                }
                else
                {
                    Console.WriteLine("No valid packets found in the file. The file might be corrupted or use a different format.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during packet analysis: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }
        
        static void DisplayReplayInfo(ReplayMetadata metadata)
        {
            var info = metadata.GameInfo;
            
            Console.WriteLine("\nReplay Information");
            Console.WriteLine("------------------");
            Console.WriteLine($"Game Version: {info.Version}");
            Console.WriteLine($"Mod: {info.Mod}");
            Console.WriteLine($"Map: {info.MapTitle}");
            Console.WriteLine($"Start Time: {info.StartTimeUtc}");
            Console.WriteLine($"End Time: {info.EndTimeUtc}");
            Console.WriteLine($"Duration: {info.Duration}");
            Console.WriteLine($"Final Game Tick: {info.FinalGameTick}");
            
            Console.WriteLine("\nPlayers:");
            foreach (var player in info.Players)
            {
                var name = info.ResolvedPlayerName(player);
                var faction = player.FactionName;
                var team = player.Team > 0 ? $"Team {player.Team}" : "No Team";
                var isHuman = player.IsHuman;
                string outcome;
                
                // Fix comparison for enum values
                if (player.Outcome == WinState.Won)
                    outcome = "Victory";
                else if (player.Outcome == WinState.Lost)
                    outcome = "Defeat";
                else
                    outcome = "Unknown";
                
                Console.WriteLine($"  - {name} ({faction}), {team}, {(isHuman ? "Human" : "Bot")}, {outcome}");
            }
        }
    }
}
