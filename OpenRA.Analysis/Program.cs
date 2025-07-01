using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Core;
using OpenRA;
using OpenRA.Traits;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Orders;

namespace OpenRA.Analysis
{
	class Program
	{
		static void Main(string[] args)
		{
			Console.WriteLine("OpenRA Analysis Tool");
			Console.WriteLine("===================");

			string modPath = null;

			// Default to the Red Alert mod directory if no path is provided
			if (args.Length > 0)
				modPath = args[0];
			else
			{
				// Look for mod directory in standard locations
				var possiblePaths = new[]
				{
					Path.Combine(Environment.CurrentDirectory, "mods", "ra"),
					Path.Combine(Path.GetDirectoryName(Environment.ProcessPath), "mods", "ra"),
					Path.Combine(Environment.CurrentDirectory, "..", "mods", "ra")
				};

				foreach (var path in possiblePaths)
				{
					if (Directory.Exists(path))
					{
						modPath = path;
						break;
					}
				}
			}

			if (modPath == null || !Directory.Exists(modPath))
			{
				Console.WriteLine("Could not find the Red Alert mod directory.");
				Console.WriteLine("Please provide the path to the Red Alert mod directory as a command-line argument.");
				return;
			}

			Console.WriteLine($"Analyzing mod at: {modPath}");

			bool continueRunning = true;
			while (continueRunning)
			{
				Console.WriteLine("\nSelect an option:");
				Console.WriteLine("1. Analyze orders from YAML files");
				Console.WriteLine("2. Show order structure information");
				Console.WriteLine("3. Show order examples");
				Console.WriteLine("4. Show network flow for orders");
				Console.WriteLine("5. List all IIssueOrder implementations");
				Console.WriteLine("6. Analyze traits from YAML files");
				Console.WriteLine("7. Analyze actor inheritance");
				Console.WriteLine("8. Exit");
				Console.Write("\nEnter your choice (1-8): ");

				var choice = Console.ReadLine();
				Console.WriteLine();

				try
				{
					switch (choice)
					{
						case "1":
							AnalyzeOrders(modPath);
							break;
						case "2":
							OrderAnalysis.DumpOrderInfo();
							OrderAnalysis.ExplainTargetTypes();
							break;
						case "3":
							OrderAnalysis.ShowOrderExample();
							break;
						case "4":
							OrderAnalysis.ShowNetworkFlow();
							break;
						case "5":
							ListAllIIssueOrderImplementations();
							break;
						case "6":
							AnalyzeTraits(modPath);
							break;
						case "7":
							AnalyzeActorInheritance(modPath);
							break;
						case "8":
							continueRunning = false;
							break;
						default:
							Console.WriteLine("Invalid choice. Please try again.");
							break;
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine($"Error: {ex.Message}");
					Console.WriteLine(ex.StackTrace);
				}

				if (continueRunning)
				{
					Console.WriteLine("\nPress any key to continue...");
					Console.ReadKey();
					Console.Clear();
					Console.WriteLine("OpenRA Analysis Tool");
					Console.WriteLine("===================");
				}
			}

			Console.WriteLine("\nThank you for using the OpenRA Analysis Tool!");
		}

		static void ListAllIIssueOrderImplementations()
		{
			Console.WriteLine("Analyzing all types that implement IIssueOrder...");

			var types = new List<Type>();

			// Check the Common assembly
			var commonAssembly = typeof(OpenRA.Mods.Common.Traits.Mobile).Assembly;
			types.AddRange(commonAssembly.GetTypes().Where(t => typeof(IIssueOrder).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract));

			// Check the Cnc assembly - use SupportPower which is public
			var cncAssembly = typeof(OpenRA.Mods.Common.Traits.SupportPower).Assembly;
			var cncTypes = cncAssembly.GetTypes().Where(t => typeof(IIssueOrder).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);
			types.AddRange(cncTypes);

			Console.WriteLine($"\nFound {types.Count} types implementing IIssueOrder:");
			foreach (var type in types.OrderBy(t => t.Name))
			{
				Console.WriteLine($"- {type.Name}");
			}

			// Extract order strings from the implementations
			var allOrders = new HashSet<string>();
			foreach (var type in types)
			{
				try
				{
					// Try to find order strings in methods
					var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					foreach (var method in methods.Where(m => m.Name == "IssueOrder" || m.Name == "ResolveOrder"))
					{
						// Check if there are any string literals in the method that might be order names
						var body = method.ToString();
						var matches = Regex.Matches(body, "\"([^\"]+)\"");
						foreach (Match match in matches)
						{
							var potentialOrder = match.Groups[1].Value;
							if (!string.IsNullOrWhiteSpace(potentialOrder) && char.IsUpper(potentialOrder[0]))
								allOrders.Add(potentialOrder);
						}
					}
				}
				catch
				{
					// Ignore reflection errors
				}
			}

			Console.WriteLine("\nPotential order strings extracted from code:");
			Console.WriteLine("===========================================");
			foreach (var order in allOrders.OrderBy(o => o))
			{
				Console.WriteLine(order);
			}
		}

		static void AnalyzeOrders(string modPath)
		{
			string rulesPath = Path.Combine(modPath, "rules");

			if (!Directory.Exists(rulesPath))
			{
				Console.WriteLine($"Error: Rules directory not found at '{rulesPath}'");
				return;
			}

			// Will contain all actor types and their supported orders
			var actorOrders = new Dictionary<string, HashSet<string>>();

			// Will contain all unique order types
			var allOrders = new HashSet<string>();

			// Known order types - let's add these as defaults since they might not be explicitly listed in traits
			AddKnownOrderTypes(allOrders);

			// Process all YAML files in the rules directory and subdirectories
			Console.WriteLine("Scanning YAML files for order definitions...");
			int processedFiles = 0;
			int skippedFiles = 0;

			// Search through traits that are known to implement IIssueOrder
			var orderTraitNames = GetOrderTraitNames();

			// Use a manual approach to parse the YAML since OpenRA's format is not fully compliant with YAML spec
			foreach (var file in Directory.GetFiles(rulesPath, "*.yaml", SearchOption.AllDirectories))
			{
				try
				{
					// Simple parsing approach for OpenRA-style YAML
					using (var reader = new StreamReader(file))
					{
						string actorName = null;
						string traitName = null;
						int actorIndent = -1;
						int traitIndent = -1;
						bool inOrdersTraitBlock = false;

						string line;
						while ((line = reader.ReadLine()) != null)
						{
							// Skip comments and empty lines
							if (string.IsNullOrWhiteSpace(line) || line.Trim().StartsWith('#'))
								continue;

							// Count leading spaces/tabs for indentation level
							int indent = CountLeadingWhitespace(line);
							string trimmedLine = line.Trim();

							// Actor definition (top level)
							if (indent == 0 && trimmedLine.EndsWith(':'))
							{
								actorName = trimmedLine.TrimEnd(':');
								actorIndent = indent;

								if (!actorOrders.ContainsKey(actorName))
									actorOrders[actorName] = new HashSet<string>();

								continue;
							}

							// Skip if we're not inside an actor definition
							if (actorName == null)
								continue;

							// If we've moved back to a previous indentation level, reset accordingly
							if (indent <= actorIndent)
							{
								actorName = null;
								traitName = null;
								inOrdersTraitBlock = false;
								continue;
							}

							// Trait definition (inside actor)
							if (indent > actorIndent && traitName == null && trimmedLine.EndsWith(':'))
							{
								traitName = trimmedLine.TrimEnd(':');
								traitIndent = indent;

								// Check if this is a trait that typically implements IIssueOrder
								inOrdersTraitBlock = orderTraitNames.Any(t => traitName.StartsWith(t));

								// Add trait-specific default orders
								AddTraitSpecificOrders(actorName, traitName, actorOrders, allOrders);

								continue;
							}

							// Skip if we're not inside a trait
							if (traitName == null)
								continue;

							// If we've moved back to actor indentation, reset trait
							if (indent <= traitIndent)
							{
								traitName = null;
								inOrdersTraitBlock = false;
								continue;
							}

							// Look for Orders or Order properties inside traits
							if (indent > traitIndent && inOrdersTraitBlock)
							{
								// Parse Orders field (potentially a list)
								if (trimmedLine.StartsWith("Orders:") || trimmedLine.StartsWith("Order:") ||
									trimmedLine.StartsWith("OrderName:") || trimmedLine.StartsWith("DeployOrderName:") ||
									trimmedLine.StartsWith("EnterOrderName:") || trimmedLine.StartsWith("AttackOrderName:"))
								{
									var colonPos = trimmedLine.IndexOf(':');
									var value = trimmedLine.Substring(colonPos + 1).Trim();

									// Check if it's a list [value1, value2, ...]
									if (value.StartsWith('[') && value.EndsWith(']'))
									{
										var ordersList = value.Trim('[', ']')
											.Split(',')
											.Select(o => o.Trim().Trim('"', '\''))
											.Where(o => !string.IsNullOrWhiteSpace(o));

										foreach (var order in ordersList)
										{
											actorOrders[actorName].Add(order);
											allOrders.Add(order);
										}
									}
									// Single value
									else
									{
										var order = value.Trim().Trim('"', '\'');
										if (!string.IsNullOrWhiteSpace(order))
										{
											actorOrders[actorName].Add(order);
											allOrders.Add(order);
										}
									}
								}
								// Look for OrderID field in targeter classes
								else if (trimmedLine.StartsWith("OrderID:"))
								{
									var value = trimmedLine.Substring("OrderID:".Length).Trim().Trim('"', '\'');
									if (!string.IsNullOrWhiteSpace(value))
									{
										actorOrders[actorName].Add(value);
										allOrders.Add(value);
									}
								}
							}
						}
					}
					processedFiles++;
				}
				catch (Exception ex)
				{
					Console.WriteLine($"Skipping {Path.GetFileName(file)}: {ex.Message}");
					skippedFiles++;
				}
			}

			// Display results
			Console.WriteLine($"\nProcessed {processedFiles} files, skipped {skippedFiles} files.");
			Console.WriteLine("\nAll unique orders found in Red Alert mod:");
			Console.WriteLine("=====================================");
			foreach (var order in allOrders.OrderBy(o => o))
			{
				Console.WriteLine(order);
			}

			Console.WriteLine($"\nFound {allOrders.Count} unique orders across {actorOrders.Count} actor types.");

			Console.WriteLine("\nWould you like to see which actors support which orders? (y/n)");
			if (Console.ReadKey().Key == ConsoleKey.Y)
			{
				Console.WriteLine("\n\nActors and their supported orders:");
				Console.WriteLine("================================");
				foreach (var actor in actorOrders.OrderBy(a => a.Key))
				{
					if (actor.Value.Count > 0)
					{
						Console.WriteLine($"\n{actor.Key}:");
						foreach (var order in actor.Value.OrderBy(o => o))
						{
							Console.WriteLine($"  - {order}");
						}
					}
				}
			}
		}

		static void AnalyzeTraits(string modPath)
		{
			string rulesPath = Path.Combine(modPath, "rules");

			if (!Directory.Exists(rulesPath))
			{
				Console.WriteLine($"Error: Rules directory not found at '{rulesPath}'");
				return;
			}

			// Will contain all actor types and their traits
			var actorTraits = new Dictionary<string, HashSet<string>>();

			// Will contain all unique trait types
			var allTraits = new HashSet<string>();

			// Process all YAML files in the rules directory and subdirectories
			Console.WriteLine("Scanning YAML files for trait definitions...");
			int processedFiles = 0;
			int skippedFiles = 0;

			// Use a manual approach to parse the YAML since OpenRA's format is not fully compliant with YAML spec
			foreach (var file in Directory.GetFiles(rulesPath, "*.yaml", SearchOption.AllDirectories))
			{
				try
				{
					// Simple parsing approach for OpenRA-style YAML
					using (var reader = new StreamReader(file))
					{
						string actorName = null;
						int actorIndent = -1;

						string line;
						while ((line = reader.ReadLine()) != null)
						{
							// Skip comments and empty lines
							if (string.IsNullOrWhiteSpace(line) || line.Trim().StartsWith('#'))
								continue;

							// Count leading spaces/tabs for indentation level
							int indent = CountLeadingWhitespace(line);
							string trimmedLine = line.Trim();

							// Actor definition (top level)
							if (indent == 0 && trimmedLine.EndsWith(':'))
							{
								actorName = trimmedLine.TrimEnd(':');
								actorIndent = indent;

								if (!actorTraits.ContainsKey(actorName))
									actorTraits[actorName] = new HashSet<string>();

								continue;
							}

							// Skip if we're not inside an actor definition
							if (actorName == null)
								continue;

							// If we've moved back to a previous indentation level, reset accordingly
							if (indent <= actorIndent)
							{
								actorName = null;
								continue;
							}

							// Trait definition (inside actor)
							if (indent > actorIndent && trimmedLine.EndsWith(':'))
							{
								var traitName = trimmedLine.TrimEnd(':');
								
								// Skip certain pseudo-traits like Inherits or -Name
								if (traitName == "Inherits" || traitName == "-Name" || traitName == "Name" || 
									traitName == "Tooltip" || traitName == "Buildable" || traitName == "Valued")
									continue;
                                
								// If it has a dash prefix, it's removing a trait
								if (traitName.StartsWith('-'))
									continue;

								// If it has an @ suffix, extract the base trait name
								int atPos = traitName.IndexOf('@');
								if (atPos > 0)
									traitName = traitName.Substring(0, atPos);

								actorTraits[actorName].Add(traitName);
								allTraits.Add(traitName);
							}
						}
					}
					processedFiles++;
				}
				catch (Exception ex)
				{
					Console.WriteLine($"Skipping {Path.GetFileName(file)}: {ex.Message}");
					skippedFiles++;
				}
			}

			// Get common trait info from assemblies for documentation
			var traitDescriptions = GetTraitDescriptions();

			// Display results
			Console.WriteLine($"\nProcessed {processedFiles} files, skipped {skippedFiles} files.");
			Console.WriteLine($"\nFound {allTraits.Count} unique traits across {actorTraits.Count} actor types.");

			Console.WriteLine("\nAll unique traits found in Red Alert mod:");
			Console.WriteLine("=====================================");
			foreach (var trait in allTraits.OrderBy(t => t))
			{
				if (traitDescriptions.TryGetValue(trait, out var description))
					Console.WriteLine($"{trait} - {description}");
				else
					Console.WriteLine(trait);
			}

			Console.WriteLine("\nWould you like to see which actors have which traits? (y/n)");
			if (Console.ReadKey().Key == ConsoleKey.Y)
			{
				Console.WriteLine("\n\nSelect an option:");
				Console.WriteLine("1. View all actors and their traits");
				Console.WriteLine("2. Search for a specific actor");
				Console.WriteLine("3. Search for actors with a specific trait");
				Console.Write("\nEnter your choice (1-3): ");

				var choice = Console.ReadLine();
				Console.WriteLine();

				switch (choice)
				{
					case "1":
						Console.WriteLine("\nActors and their traits:");
						Console.WriteLine("=======================");
						foreach (var actor in actorTraits.OrderBy(a => a.Key))
						{
							if (actor.Value.Count > 0)
							{
								Console.WriteLine($"\n{actor.Key}:");
								foreach (var trait in actor.Value.OrderBy(t => t))
								{
									Console.WriteLine($"  - {trait}");
								}
							}
						}
						break;

					case "2":
						Console.Write("Enter actor name to search for (partial matches supported): ");
						var actorSearch = Console.ReadLine();
						
						var matchingActors = actorTraits.Keys
							.Where(k => k.Contains(actorSearch, StringComparison.OrdinalIgnoreCase))
							.OrderBy(k => k)
							.ToList();

						if (matchingActors.Count == 0)
						{
							Console.WriteLine($"No actors found matching '{actorSearch}'");
						}
						else
						{
							Console.WriteLine($"\nFound {matchingActors.Count} matching actors:");
							foreach (var actor in matchingActors)
							{
								Console.WriteLine($"\n{actor}:");
								foreach (var trait in actorTraits[actor].OrderBy(t => t))
								{
									Console.WriteLine($"  - {trait}");
								}
							}
						}
						break;

					case "3":
						Console.Write("Enter trait name to search for (partial matches supported): ");
						var traitSearch = Console.ReadLine();
						
						var matchingTraits = allTraits
							.Where(t => t.Contains(traitSearch, StringComparison.OrdinalIgnoreCase))
							.OrderBy(t => t)
							.ToList();

						if (matchingTraits.Count == 0)
						{
							Console.WriteLine($"No traits found matching '{traitSearch}'");
						}
						else
						{
							Console.WriteLine($"\nFound {matchingTraits.Count} matching traits:");
							foreach (var trait in matchingTraits)
							{
								Console.WriteLine($"\n{trait}:");
								Console.WriteLine("Used by these actors:");
								var actorsWithTrait = actorTraits
									.Where(a => a.Value.Contains(trait))
									.Select(a => a.Key)
									.OrderBy(a => a)
									.ToList();
									
								if (actorsWithTrait.Count > 0)
								{
									foreach (var actor in actorsWithTrait)
									{
										Console.WriteLine($"  - {actor}");
									}
								}
								else
								{
									Console.WriteLine("  (No actors use this trait directly)");
								}
							}
						}
						break;

					default:
						Console.WriteLine("Invalid choice.");
						break;
				}
			}

			// Offer an option to export to a file
			Console.WriteLine("\nWould you like to export all trait data to a file? (y/n)");
			if (Console.ReadKey().Key == ConsoleKey.Y)
			{
				Console.WriteLine("\nExporting data...");
				var outputPath = Path.Combine(Environment.CurrentDirectory, "TraitAnalysis.txt");
				
				using (var writer = new StreamWriter(outputPath))
				{
					writer.WriteLine("OpenRA Trait Analysis");
					writer.WriteLine("====================");
					writer.WriteLine($"Mod Path: {modPath}");
					writer.WriteLine($"Date: {DateTime.Now}");
					writer.WriteLine($"Found {allTraits.Count} unique traits across {actorTraits.Count} actor types.\n");
					
					writer.WriteLine("All Traits:");
					writer.WriteLine("===========");
					foreach (var trait in allTraits.OrderBy(t => t))
					{
						if (traitDescriptions.TryGetValue(trait, out var description))
							writer.WriteLine($"{trait} - {description}");
						else
							writer.WriteLine(trait);
					}
					
					writer.WriteLine("\nActors and their Traits:");
					writer.WriteLine("======================");
					foreach (var actor in actorTraits.OrderBy(a => a.Key))
					{
						if (actor.Value.Count > 0)
						{
							writer.WriteLine($"\n{actor.Key}:");
							foreach (var trait in actor.Value.OrderBy(t => t))
							{
								writer.WriteLine($"  - {trait}");
							}
						}
					}
				}
				
				Console.WriteLine($"Data exported to: {outputPath}");
			}
		}

		private static Dictionary<string, string> GetTraitDescriptions()
		{
			var descriptions = new Dictionary<string, string>();
			
			try
			{
				// Get descriptions from assemblies
				var commonAssembly = typeof(OpenRA.Mods.Common.Traits.Mobile).Assembly;
				var traitInfos = commonAssembly.GetTypes()
					.Where(t => typeof(TraitInfo).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
					.ToList();
				
				foreach (var traitInfo in traitInfos)
				{
					var name = traitInfo.Name;
					if (name.EndsWith("Info"))
						name = name.Substring(0, name.Length - 4);
					
					// Try to get description from DescAttribute
					var descAttrs = traitInfo.GetCustomAttributes(typeof(DescAttribute), false)
						.Cast<DescAttribute>()
						.FirstOrDefault();
					
					if (descAttrs != null && descAttrs.Lines.Length > 0)
					{
						descriptions[name] = string.Join(" ", descAttrs.Lines);
					}
					else
					{
						// Create description from class name by adding spaces before capital letters
						var autoDesc = Regex.Replace(name, "([a-z])([A-Z])", "$1 $2");
						descriptions[name] = autoDesc;
					}
				}
			}
			catch
			{
				// Ignore errors in getting descriptions
			}
			
			return descriptions;
		}

		static string[] GetOrderTraitNames()
		{
			return new[]
			{
				"Mobile", "AttackBase", "Attack", "Harvester", "Transforms", "TransformsInto",
				"Passenger", "Cargo", "EntersTunnels", "Demolition", "Capturable", "CaptureManager",
				"SelfHealing", "Repairable", "Rearmable", "ReloadAmmo", "Resupply", "Aircraft",
				"DockManager", "Dock", "Production", "GrantExternalConditionPower", "ChronoshiftPower",
				"IronCurtainPower", "SonarPulsePower", "ParatroopersPower", "NukePower", "Sellable",
				"Guard", "Huntable", "ProximityCaptor", "Spy", "Infiltrates", "Engineer",
				"RepairsBridges", "Building", "Carryall", "Chronoshiftable", "IronCurtainable",
				"GpsPower", "AirstrikePower", "SupportPower", "MadTank", "GrantConditionOnDeploy",
				"DeployToUpgrade", "CashTrickler", "AutoTarget", "WithSpriteBody", "WithMakeAnimation",
				"ProvidesPrerequisite", "RequiresPrerequisite"
			};
		}

		static void AddKnownOrderTypes(HashSet<string> allOrders)
		{
			// Common orders
			var commonOrders = new[] {
				"Move", "Attack", "Stop", "Scatter", "Deploy", "DeployTransform", "Sell", "Repair",
				"Power", "Production", "Guard", "Harvest", "Enter", "EnterTransport", "Unload",
				"Demolish", "Capture", "Infiltrate", "C4", "AttackMove", "DeployToUpgrade", "Dock",
				"ReturnToBase", "GrantConditionOnDeploy", "GrantUpgrade", "StartBuildingRepair",
				"PlaceBuilding", "Heal", "Chronosphere", "IronCurtain", "RepairBridge", "Steal"
			};

			// Special powers
			var specialPowers = new[] {
				"Chronoshift", "IronCurtain", "GpsPower", "ParatroopersPower", "NukePower",
				"Detonate", "DetonateAttack", "Sonar", "SpyPlane", "Airstrike", "AdvancedChronoshift"
			};

			foreach (var order in commonOrders.Concat(specialPowers))
				allOrders.Add(order);
		}

		static void AddTraitSpecificOrders(string actorName, string traitName, Dictionary<string, HashSet<string>> actorOrders, HashSet<string> allOrders)
		{
			var traitOrderMap = new Dictionary<string, string[]>
			{
				{ "Mobile", new[] { "Move", "Stop", "Scatter", "AttackMove" } },
				{ "AttackBase", new[] { "Attack" } },
				{ "Attack", new[] { "Attack" } },
				{ "Transforms", new[] { "Deploy", "DeployTransform" } },
				{ "Production", new[] { "Production", "PlaceBuilding" } },
				{ "Sellable", new[] { "Sell" } },
				{ "Harvester", new[] { "Harvest", "ReturnToBase" } },
				{ "Cargo", new[] { "Enter", "EnterTransport", "Unload", "Exit" } },
				{ "Passenger", new[] { "Enter", "EnterTransport", "Unload", "Exit" } },
				{ "Capturable", new[] { "Capture" } },
				{ "Engineer", new[] { "Capture" } },
				{ "Demolition", new[] { "C4" } },
				{ "Guard", new[] { "Guard" } },
				{ "Spy", new[] { "Infiltrate" } },
				{ "Infiltrates", new[] { "Infiltrate" } },
				{ "ChronoshiftPower", new[] { "Chronoshift" } },
				{ "IronCurtainPower", new[] { "IronCurtain" } },
				{ "NukePower", new[] { "NukePower" } },
				{ "ParatroopersPower", new[] { "ParatroopersPower" } },
				{ "MadTank", new[] { "Detonate", "DetonateAttack" } },
				{ "Dock", new[] { "Dock", "Repair" } },
				{ "DockManager", new[] { "Dock", "Repair" } },
				{ "Repairable", new[] { "Repair" } },
				{ "RepairsBridges", new[] { "RepairBridge" } },
				{ "GrantConditionOnDeploy", new[] { "DeployToUpgrade" } }
			};

			foreach (var mapping in traitOrderMap)
			{
				if (traitName.StartsWith(mapping.Key))
				{
					foreach (var order in mapping.Value)
					{
						actorOrders[actorName].Add(order);
					}
				}
			}
		}

		static int CountLeadingWhitespace(string line)
		{
			int count = 0;
			foreach (char c in line)
			{
				if (c == ' ')
					count++;
				else if (c == '\t')
					count += 4; // Count tabs as 4 spaces
				else
					break;
			}
			return count;
		}

		static void AnalyzeActorInheritance(string modPath)
		{
			string rulesPath = Path.Combine(modPath, "rules");

			if (!Directory.Exists(rulesPath))
			{
				Console.WriteLine($"Error: Rules directory not found at '{rulesPath}'");
				return;
			}

			// Map of actor name to its direct parents
			var inheritanceMap = new Dictionary<string, HashSet<string>>();
			
			// Map of actor name to actors that inherit from it
			var reverseInheritanceMap = new Dictionary<string, HashSet<string>>();

			// Process all YAML files in the rules directory and subdirectories
			Console.WriteLine("Scanning YAML files for actor inheritance...");
			int processedFiles = 0;
			int skippedFiles = 0;

			foreach (var file in Directory.GetFiles(rulesPath, "*.yaml", SearchOption.AllDirectories))
			{
				try
				{
					using (var reader = new StreamReader(file))
					{
						string currentActor = null;
						int actorIndent = -1;

						string line;
						while ((line = reader.ReadLine()) != null)
						{
							// Skip comments and empty lines
							if (string.IsNullOrWhiteSpace(line) || line.Trim().StartsWith('#'))
								continue;

							// Count leading spaces/tabs for indentation level
							int indent = CountLeadingWhitespace(line);
							string trimmedLine = line.Trim();

							// Actor definition (top level)
							if (indent == 0 && trimmedLine.EndsWith(':'))
							{
								currentActor = trimmedLine.TrimEnd(':');
								actorIndent = indent;

								if (!inheritanceMap.ContainsKey(currentActor))
									inheritanceMap[currentActor] = new HashSet<string>();

								continue;
							}

							// Skip if we're not inside an actor definition
							if (currentActor == null)
								continue;

							// If we've moved back to a previous indentation level, reset actor
							if (indent <= actorIndent)
							{
								currentActor = null;
								continue;
							}

							// Look for Inherits lines
							if (indent > actorIndent && trimmedLine.StartsWith("Inherits"))
							{
								var colonPos = trimmedLine.IndexOf(':');
								if (colonPos > -1)
								{
									var value = trimmedLine.Substring(colonPos + 1).Trim().Trim('"', '\'');
									if (!string.IsNullOrWhiteSpace(value))
									{
										// Add inheritance relationship
										inheritanceMap[currentActor].Add(value);
										
										// Add to reverse map
										if (!reverseInheritanceMap.ContainsKey(value))
											reverseInheritanceMap[value] = new HashSet<string>();
										reverseInheritanceMap[value].Add(currentActor);
									}
								}
							}
						}
					}
					processedFiles++;
				}
				catch (Exception ex)
				{
					Console.WriteLine($"Skipping {Path.GetFileName(file)}: {ex.Message}");
					skippedFiles++;
				}
			}

			Console.WriteLine($"\nProcessed {processedFiles} files, skipped {skippedFiles} files.");
			Console.WriteLine($"Found {inheritanceMap.Count} actor definitions with inheritance relationships.\n");

			// Display hierarchy options
			bool showingHierarchy = true;
			while (showingHierarchy)
			{
				Console.WriteLine("\nSelect how to view the actor hierarchy:");
				Console.WriteLine("1. Show complete hierarchy");
				Console.WriteLine("2. Show inheritance tree for specific actor");
				Console.WriteLine("3. Show actors that inherit from a specific actor");
				Console.WriteLine("4. Export hierarchy diagram to file");
				Console.WriteLine("5. Return to main menu");
				Console.Write("\nEnter your choice (1-5): ");

				var choice = Console.ReadLine();
				Console.WriteLine();

				switch (choice)
				{
					case "1":
						ShowCompleteHierarchy(inheritanceMap);
						break;

					case "2":
						Console.Write("Enter actor name (partial matches supported): ");
						var actorSearch = Console.ReadLine();
						
						var matchingActors = inheritanceMap.Keys
							.Where(k => k.Contains(actorSearch, StringComparison.OrdinalIgnoreCase))
							.OrderBy(k => k)
							.ToList();

						if (matchingActors.Count == 0)
						{
							Console.WriteLine($"No actors found matching '{actorSearch}'");
						}
						else if (matchingActors.Count > 1)
						{
							Console.WriteLine($"\nFound {matchingActors.Count} matching actors:");
							foreach (var actor in matchingActors)
								Console.WriteLine($"- {actor}");
								
							Console.Write("\nEnter exact actor name from the list above: ");
							var exactActor = Console.ReadLine();
							if (inheritanceMap.ContainsKey(exactActor))
								ShowActorHierarchy(exactActor, inheritanceMap);
							else
								Console.WriteLine("Invalid actor name.");
						}
						else
						{
							ShowActorHierarchy(matchingActors[0], inheritanceMap);
						}
						break;

					case "3":
						Console.Write("Enter actor name (partial matches supported): ");
						actorSearch = Console.ReadLine();
						
						matchingActors = reverseInheritanceMap.Keys
							.Where(k => k.Contains(actorSearch, StringComparison.OrdinalIgnoreCase))
							.OrderBy(k => k)
							.ToList();

						if (matchingActors.Count == 0)
						{
							Console.WriteLine($"No actors found matching '{actorSearch}'");
						}
						else if (matchingActors.Count > 1)
						{
							Console.WriteLine($"\nFound {matchingActors.Count} matching actors:");
							foreach (var actor in matchingActors)
								Console.WriteLine($"- {actor}");
								
							Console.Write("\nEnter exact actor name from the list above: ");
							var exactActor = Console.ReadLine();
							if (reverseInheritanceMap.ContainsKey(exactActor))
								ShowInheritingActors(exactActor, reverseInheritanceMap);
							else
								Console.WriteLine("Invalid actor name.");
						}
						else
						{
							ShowInheritingActors(matchingActors[0], reverseInheritanceMap);
						}
						break;

					case "4":
						ExportHierarchyDiagram(inheritanceMap);
						break;

					case "5":
						showingHierarchy = false;
						break;

					default:
						Console.WriteLine("Invalid choice. Please try again.");
						break;
				}
			}
		}

		static void ShowActorHierarchy(string actor, Dictionary<string, HashSet<string>> inheritanceMap)
		{
			Console.WriteLine($"\nInheritance hierarchy for {actor}:");
			Console.WriteLine("============================");
			
			// Show what this actor inherits from (parents)
			var parents = inheritanceMap[actor];
			if (parents.Count > 0)
			{
				Console.WriteLine("Inherits from:");
				foreach (var parent in parents.OrderBy(p => p))
				{
					Console.WriteLine($"  {actor} --> {parent}");
					// Recursively show parent's inheritance
					ShowParentInheritance(parent, inheritanceMap, "    ", actor);
				}
			}
			else
			{
				Console.WriteLine("(Base actor - does not inherit from any other actors)");
			}
		}

		static void ShowParentInheritance(string actor, Dictionary<string, HashSet<string>> inheritanceMap, string indent, string childActor)
		{
			if (inheritanceMap.TryGetValue(actor, out var parents) && parents.Count > 0)
			{
				foreach (var parent in parents.OrderBy(p => p))
				{
					Console.WriteLine($"{indent}{actor} --> {parent}");
					ShowParentInheritance(parent, inheritanceMap, indent + "  ", actor);
				}
			}
		}

		static void ShowInheritingActors(string actor, Dictionary<string, HashSet<string>> reverseInheritanceMap)
		{
			if (!reverseInheritanceMap.ContainsKey(actor))
			{
				Console.WriteLine($"\nNo actors inherit from {actor}");
				return;
			}

			Console.WriteLine($"\nActors that inherit from {actor}:");
			Console.WriteLine("===============================");
			
			foreach (var child in reverseInheritanceMap[actor].OrderBy(a => a))
				PrintInheritingActorTree(child, reverseInheritanceMap, "  ", actor);
		}

		static void PrintActorTree(string actor, Dictionary<string, HashSet<string>> inheritanceMap, string indent = "  ", string parentActor = null)
		{
			if (parentActor != null)
				Console.WriteLine($"{indent}{parentActor} --> {actor}");
			else
				Console.WriteLine($"{indent}{actor}");
			
			// Find actors that inherit from this one
			var children = inheritanceMap
				.Where(kvp => kvp.Value.Contains(actor))
				.Select(kvp => kvp.Key)
				.OrderBy(k => k);

			foreach (var child in children)
				PrintActorTree(child, inheritanceMap, indent + "  ", actor);
		}

		static void PrintInheritingActorTree(string actor, Dictionary<string, HashSet<string>> reverseInheritanceMap, string indent = "  ", string parentActor = null)
		{
			if (parentActor != null)
				Console.WriteLine($"{indent}{parentActor} --> {actor}");
			else
				Console.WriteLine($"{indent}{actor}");
			
			if (reverseInheritanceMap.TryGetValue(actor, out var children))
			{
				foreach (var child in children.OrderBy(c => c))
					PrintInheritingActorTree(child, reverseInheritanceMap, indent + "  ", actor);
			}
		}

		static void ShowCompleteHierarchy(Dictionary<string, HashSet<string>> inheritanceMap)
		{
			// Find root actors (those that don't inherit from anyone)
			var allActors = inheritanceMap.Keys.ToHashSet();
			var inheritedActors = inheritanceMap.Values.SelectMany(v => v).ToHashSet();
			var rootActors = allActors.Except(inheritedActors).OrderBy(a => a).ToList();

			Console.WriteLine("Complete Actor Hierarchy:");
			Console.WriteLine("=======================");
			
			foreach (var root in rootActors)
				PrintActorTree(root, inheritanceMap);

			// Also show any disconnected inheritance relationships
			var handledActors = new HashSet<string>();
			foreach (var actor in inheritanceMap.Keys.OrderBy(k => k))
			{
				if (!handledActors.Contains(actor))
				{
					foreach (var parent in inheritanceMap[actor])
					{
						if (!inheritanceMap.ContainsKey(parent))
						{
							Console.WriteLine($"\nDisconnected inheritance:");
							Console.WriteLine($"  {actor} --> {parent} (undefined actor)");
							handledActors.Add(actor);
						}
					}
				}
			}
		}

		static void ExportHierarchyDiagram(Dictionary<string, HashSet<string>> inheritanceMap)
		{
			Console.WriteLine("Exporting actor hierarchy diagram...");
			var outputPath = Path.Combine(Environment.CurrentDirectory, "ActorHierarchy.dot");
			
			using (var writer = new StreamWriter(outputPath))
			{
				// Write DOT file header
				writer.WriteLine("digraph ActorHierarchy {");
				writer.WriteLine("  rankdir=TB;");  // Top to bottom direction
				writer.WriteLine("  node [shape=box, style=filled, fillcolor=lightgray];");
				writer.WriteLine("  edge [dir=back];");  // Arrows point from parent to child
				
				// Find root nodes (no parents) and mark them differently
				var allActors = inheritanceMap.Keys.ToHashSet();
				var inheritedActors = inheritanceMap.Values.SelectMany(v => v).ToHashSet();
				var rootActors = allActors.Except(inheritedActors).ToHashSet();
				
				// Style root nodes differently
				foreach (var root in rootActors)
				{
					writer.WriteLine($"  \"{root}\" [fillcolor=lightblue];");
				}
				
				// Style undefined parent nodes differently
				var undefinedParents = inheritanceMap.Values
					.SelectMany(v => v)
					.Where(p => !inheritanceMap.ContainsKey(p))
					.Distinct()
					.ToHashSet();
					
				foreach (var undefined in undefinedParents)
				{
					writer.WriteLine($"  \"{undefined}\" [fillcolor=pink, style=\"filled,dashed\"];");
				}

				// Write inheritance relationships (reversed arrow direction for better visual hierarchy)
				foreach (var actor in inheritanceMap)
				{
					foreach (var parent in actor.Value)
					{
						writer.WriteLine($"  \"{parent}\" -> \"{actor.Key}\";");
					}
				}

				// Try to enforce some ordering of nodes at the same level
				writer.WriteLine("  { rank=same; ");
				foreach (var root in rootActors.OrderBy(r => r))
				{
					writer.Write($"\"{root}\"; ");
				}
				writer.WriteLine("}");
				
				writer.WriteLine("}");
			}
			
			Console.WriteLine($"Exported DOT file to: {outputPath}");
			Console.WriteLine("You can visualize this file using Graphviz or an online DOT visualizer.");
			Console.WriteLine("The diagram shows:");
			Console.WriteLine("- Light blue boxes: Base actors (no parents)");
			Console.WriteLine("- Light gray boxes: Normal actors");
			Console.WriteLine("- Pink dashed boxes: Referenced but undefined actors");
			Console.WriteLine("Arrows point from parent to child actors.");
		}
	}
}
