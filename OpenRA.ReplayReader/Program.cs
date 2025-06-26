using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OpenRA.ReplayReader
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("OpenRA Replay Reader");
            Console.WriteLine("===================");

            if (args.Length == 0)
            {
                ShowUsage();
                return;
            }

            string command = args[0].ToLower();

            try
            {
                switch (command)
                {
                    case "analyze":
                        if (args.Length < 2)
                        {
                            Console.WriteLine("Error: No replay file specified for analysis.");
                            ShowUsage();
                            return;
                        }
                        AnalyzeReplayFile(args[1]);
                        break;

                    case "extract-orders":
                        if (args.Length < 2)
                        {
                            Console.WriteLine("Error: No replay file specified for order extraction.");
                            ShowUsage();
                            return;
                        }
                        string outputPath = args.Length > 2 ? args[2] : null;
                        ExtractOrders(args[1], outputPath);
                        break;

                    case "show-order-types":
                        ShowOrderTypes();
                        break;

                    case "run-tests":
                        RunDecoderTests();
                        break;

                    default:
                        Console.WriteLine($"Error: Unknown command '{command}'");
                        ShowUsage();
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }

        static void ShowUsage()
        {
            Console.WriteLine("\nUsage:");
            Console.WriteLine("  OpenRA.ReplayReader analyze <replayFile>");
            Console.WriteLine("  OpenRA.ReplayReader extract-orders <replayFile> [outputFile]");
            Console.WriteLine("  OpenRA.ReplayReader show-order-types");
            Console.WriteLine("  OpenRA.ReplayReader run-tests");
        }

        static void RunDecoderTests()
        {
            Console.WriteLine("Running OrderDecoder tests to verify functionality...\n");
            OrderDecoderTests.RunAllTests();
        }

        static void ShowOrderTypes()
        {
            Console.WriteLine("\nAvailable Order Types in OpenRA:");

            // Movement orders
            Console.WriteLine("\nMovement Orders:");
            foreach (var order in OrderAnalyzer.MovementOrders.OrderBy(o => o))
            {
                Console.WriteLine($"  {order}");
            }

            // Combat orders
            Console.WriteLine("\nCombat Orders:");
            foreach (var order in OrderAnalyzer.CombatOrders.OrderBy(o => o))
            {
                Console.WriteLine($"  {order}");
            }

            // Production orders
            Console.WriteLine("\nProduction Orders:");
            foreach (var order in OrderAnalyzer.ProductionOrders.OrderBy(o => o))
            {
                Console.WriteLine($"  {order}");
            }

            // Support orders
            Console.WriteLine("\nSupport Orders:");
            foreach (var order in OrderAnalyzer.SupportOrders.OrderBy(o => o))
            {
                Console.WriteLine($"  {order}");
            }

            // Special power orders
            Console.WriteLine("\nSpecial Power Orders:");
            foreach (var order in OrderAnalyzer.SpecialPowerOrders.OrderBy(o => o))
            {
                Console.WriteLine($"  {order}");
            }

            // Network orders
            Console.WriteLine("\nNetwork Orders:");
            foreach (var order in OrderAnalyzer.NetworkOrders.OrderBy(o => o))
            {
                Console.WriteLine($"  {order}");
            }

            // Communication orders
            Console.WriteLine("\nCommunication Orders:");
            foreach (var order in OrderAnalyzer.CommunicationOrders.OrderBy(o => o))
            {
                Console.WriteLine($"  {order}");
            }
        }

        static void ExtractOrders(string replayPath, string outputPath = null)
        {
            Console.WriteLine($"Extracting orders from replay: {replayPath}");

            var orders = OrderAnalyzer.ParseReplayOrders(replayPath);

            if (orders.Count == 0)
            {
                Console.WriteLine("No orders found in the replay file.");
                return;
            }

            Console.WriteLine($"Found {orders.Count} orders.");

            // Generate default output filename if not provided
            string jsonFilePath = outputPath;
            if (string.IsNullOrEmpty(jsonFilePath))
            {
                var directory = Path.GetDirectoryName(replayPath);
                var filename = Path.GetFileNameWithoutExtension(replayPath);
                jsonFilePath = Path.Combine(directory, $"{filename}.json");
            }

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

                // Parse orders from the replay
                Console.WriteLine("\nParsing replay file...");
                var orders = OrderAnalyzer.ParseReplayOrders(replayPath);

                Console.WriteLine($"Total orders found: {orders.Count}");

                // Group orders by type for analysis
                var ordersByType = orders
                    .GroupBy(o => o.OrderType)
                    .OrderByDescending(g => g.Count())
                    .ToList();

                Console.WriteLine("\nOrder type distribution:");
                foreach (var group in ordersByType)
                {
                    Console.WriteLine($"  {group.Key}: {group.Count()} orders");
                }

                // Show examples of different order types with decoded ExtraData
                Console.WriteLine("\nOrder examples with decoded ExtraData:");

                // Find examples of each order category
                ShowCategoryExamples("Movement", orders, OrderAnalyzer.MovementOrders);
                ShowCategoryExamples("Combat", orders, OrderAnalyzer.CombatOrders);
                ShowCategoryExamples("Production", orders, OrderAnalyzer.ProductionOrders);
                ShowCategoryExamples("Support", orders, OrderAnalyzer.SupportOrders);
                ShowCategoryExamples("Special Power", orders, OrderAnalyzer.SpecialPowerOrders);
                ShowCategoryExamples("Network", orders, OrderAnalyzer.NetworkOrders);
                ShowCategoryExamples("Communication", orders, OrderAnalyzer.CommunicationOrders);

                // Save all orders to JSON for further analysis
                SaveOrdersToJson(replayPath, orders);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading replay file: {ex.Message}");
                throw;
            }
        }

        static void ShowCategoryExamples(string category, List<OrderInfo> orders, HashSet<string> categoryOrders)
        {
            var categoryExamples = new Dictionary<string, OrderInfo>();

            // Find one example of each order type in this category
            foreach (var orderType in categoryOrders)
            {
                var example = orders.FirstOrDefault(o => string.Equals(o.OrderType, orderType, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(o.ExtraData));
                if (example != null)
                {
                    categoryExamples[orderType] = example;
                }
            }

            if (categoryExamples.Count > 0)
            {
                Console.WriteLine($"\n{category} Order Examples:");
                foreach (var pair in categoryExamples.Take(3)) // Limit to 3 examples per category
                {
                    Console.WriteLine($"\n  {pair.Key} Example:");
                    Console.WriteLine($"  {pair.Value}");
                    Console.WriteLine("  --------");
                }

                if (categoryExamples.Count > 3)
                {
                    Console.WriteLine($"  ... and {categoryExamples.Count - 3} more {category} order types");
                }
            }
        }

        static void SaveOrdersToJson(string replayPath, List<OrderInfo> orders)
        {
            var directory = Path.GetDirectoryName(replayPath);
            var filename = Path.GetFileNameWithoutExtension(replayPath);
            var jsonFilePath = Path.Combine(directory, $"{filename}.json");

            try
            {
                var json = JsonSerializer.Serialize(orders, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(jsonFilePath, json);
                Console.WriteLine($"\nOrders saved to JSON file: {jsonFilePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving orders to JSON: {ex.Message}");
            }
        }
    }
}
