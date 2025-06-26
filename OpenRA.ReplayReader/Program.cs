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
            
            // First run decoder tests
            OrderDecoderTests.RunTests();
            
            string replayPath;
            
            if (args.Length == 0)
            {
                Console.WriteLine("\nNo replay file specified. Using default test replay file.");
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
                
                // Parse metadata
                using (var stream = fileInfo.OpenRead())
                {
                    Console.WriteLine("\nParsing replay file...");
                    var orders = OrderAnalyzer.ParseReplayOrders(replayPath);
                    var setRallyPointOrders = orders.Where(o => o.OrderType == "SetRallyPoint").ToList();
                    
                    if (setRallyPointOrders.Any())
                    {
                        Console.WriteLine("\nSetRallyPoint orders found:");
                        foreach (var order in setRallyPointOrders)
                        {
                            Console.WriteLine(order.ToString());
                            Console.WriteLine("--------");
                        }
                    }
                    else
                    {
                        Console.WriteLine("\nNo SetRallyPoint orders found in replay.");
                    }
                    
                    // Save all orders to JSON for further analysis
                    SaveOrdersToJson(replayPath, orders);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading replay file: {ex.Message}");
                throw;
            }
        }
    }
}
