using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace OpenRA.ReplayReader
{
    public static class OrderAnalyzer
    {
        // Movement orders
        public static readonly HashSet<string> MovementOrders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Move", "Stop", "Scatter", "ForceMove", "ForceMoveIntoTarget", "Infiltrate",
            "Enter", "EnterTransport", "Exit", "Unload", "Dock", "ReturnToBase",
            "AttackMove", "AssaultMove", "Patrol"
        };

        // Combat orders
        public static readonly HashSet<string> CombatOrders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Attack", "AttackMove", "ForceAttack", "Guard", "AssaultMove", "Detonate",
            "DetonateAttack", "C4", "Demolish", "Explode", "TargetPoint", "TargetLineMove",
            "Combat_AttackMove"
        };

        // Production orders
        public static readonly HashSet<string> ProductionOrders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "StartProduction", "PauseProduction", "CancelProduction", "BuildBuilding",
            "PlaceBuilding", "DeployTransform", "Undeploy", "PlaceObject", "DeployMcv",
            "Production", "Deploy", "GrantUpgrade", "Produce", "BuildArms", "BuildNaval",
            "BuildVehicle", "BuildAircraft", "TrainInfantry", "SetPrimaryBuilding",
            "QueueUnit", "StartConstruction", "ToggleProduction", "CreateGroup"
        };

        // Support orders
        public static readonly HashSet<string> SupportOrders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Power", "Repair", "Sell", "Capture", "Heal", "SetRallyPoint", "Harvest",
            "ReturnToRefinery", "DeliverCash", "DeliverExperience", "ToggleProduction",
            "Chronoshift", "IronCurtain", "GpsPower", "ParatroopersPower", "NukePower",
            "Sonar", "SpyPlane", "Airstrike", "AdvancedChronoshift", "GrantExternalCondition",
            "RepairBridge", "Steal", "Infiltrate", "Disguise", "Demolish", "DeployTransform",
            "DeployToUpgrade", "Chronosphere", "IronCurtain", "NukePowerInfoOrder", "DropSpecialPower"
        };

        // Special power orders
        public static readonly HashSet<string> SpecialPowerOrders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Chronoshift", "IronCurtain", "GpsPower", "ParatroopersPower", "NukePower",
            "Sonar", "SpyPlane", "Airstrike", "AdvancedChronoshift", "GrantExternalCondition",
            "IonCannon", "ArtilleryBarrage", "Paradrop", "ElectricBolt", "AirstrikePower",
            "GrantUpgradePower", "NukePowerInfoOrder", "DropSpecialPower"
        };

        // Network and system orders
        public static readonly HashSet<string> NetworkOrders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SyncHash", "SyncConnectionQuality", "StateHash", "Disconnect", "Ping", "Handshake",
            "Tick", "Ack", "SyncLobbyClients", "SyncInfo", "PauseGame", "Command"
        };

        // Communication orders
        public static readonly HashSet<string> CommunicationOrders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Chat", "TeamChat", "FluentMessage"
        };

        // Order categorization
        public static string CategorizeOrder(string orderType)
        {
            // Special handling for the combat marker we add
            if (orderType == "Combat_AttackMove")
            {
                return "Combat";
            }

            if (MovementOrders.Contains(orderType))
            {
                return "Movement";
            }
            
            if (CombatOrders.Contains(orderType))
            {
                return "Combat";
            }
            
            if (ProductionOrders.Contains(orderType))
            {
                return "Production";
            }
            
            if (SupportOrders.Contains(orderType))
            {
                return "Support";
            }
            
            if (SpecialPowerOrders.Contains(orderType))
            {
                return "SpecialPower";
            }
            
            if (NetworkOrders.Contains(orderType))
            {
                return "Network";
            }
            
            if (CommunicationOrders.Contains(orderType))
            {
                return "Communication";
            }

            // Handle unknown order types
            if (orderType.StartsWith("Unknown_", StringComparison.OrdinalIgnoreCase))
            {
                var baseType = orderType.Substring(8);
                if (NetworkOrders.Contains(baseType))
                {
                    return "Network";
                }
            }

            return "Other";
        }
        
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

                            // Handle various order types
                            if (orderType == OrderType.SyncHash || orderType == OrderType.Disconnect ||
                                orderType == OrderType.Ping || orderType == OrderType.Handshake ||
                                orderType == OrderType.TickScale)
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

                                        // Read any extra data that's present
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
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }

            return orders;
        }
    }
}
