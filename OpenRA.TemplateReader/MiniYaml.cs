using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace OpenRA
{
    // This is a simplified mock of the MiniYaml class that provides just enough functionality
    // to parse basic YAML files containing template definitions
    public class MiniYaml
    {
        public string Value { get; private set; }
        public List<MiniYamlNode> Nodes { get; private set; }
        public Dictionary<string, MiniYaml> NodesDict { get; private set; }
        public string Location { get; private set; }

        public MiniYaml(string value, List<MiniYamlNode> nodes = null, string location = null)
        {
            Value = value ?? "";
            Nodes = nodes ?? new List<MiniYamlNode>();
            NodesDict = Nodes.ToDictionary(n => n.Key, n => n.Value, StringComparer.OrdinalIgnoreCase);
            Location = location;
        }

        public static List<MiniYamlNode> FromString(string yaml, string location = null)
        {
            return FromLines(yaml.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None), location);
        }

        public static List<MiniYamlNode> FromLines(string[] lines, string location = null)
        {
            var levels = new List<List<MiniYamlNode>>();
            levels.Add(new List<MiniYamlNode>());

            var currentDepth = 0;
            var noNewLine = false;

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                // Skip comments and empty lines
                if (string.IsNullOrWhiteSpace(line) || line.Trim().StartsWith("#"))
                    continue;

                // Count indentation level
                var indent = 0;
                while (indent < line.Length && line[indent] == '\t')
                    indent++;

                line = line.Substring(indent);

                // Parse key-value
                var keyValue = line.Split(new[] { ':' }, 2);
                var key = keyValue[0].Trim();
                var value = keyValue.Length > 1 ? keyValue[1].Trim() : "";

                // Remove quotes from keys
                if (key.Length >= 2 && key[0] == '"' && key[key.Length - 1] == '"')
                    key = key.Substring(1, key.Length - 2);

                if (indent > currentDepth)
                {
                    levels.Add(new List<MiniYamlNode>());
                    currentDepth = indent;
                }
                else if (indent < currentDepth)
                {
                    // Close levels
                    while (currentDepth > indent)
                    {
                        var nodes = levels[levels.Count - 1];
                        levels.RemoveAt(levels.Count - 1);
                        
                        if (levels.Count > 0 && levels[levels.Count - 1].Count > 0)
                        {
                            var lastNode = levels[levels.Count - 1][levels[levels.Count - 1].Count - 1];
                            lastNode.Value.Nodes.AddRange(nodes);
                            
                            // Update the nodes dictionary
                            foreach (var node in nodes)
                                lastNode.Value.NodesDict.Add(node.Key, node.Value);
                        }
                        
                        currentDepth--;
                    }
                }

                var yaml = new MiniYaml(value, new List<MiniYamlNode>(), location);
                levels[levels.Count - 1].Add(new MiniYamlNode(key, yaml, location));
            }

            // Close remaining levels
            while (levels.Count > 1)
            {
                var nodes = levels[levels.Count - 1];
                levels.RemoveAt(levels.Count - 1);
                
                if (levels[levels.Count - 1].Count > 0)
                {
                    var lastNode = levels[levels.Count - 1][levels[levels.Count - 1].Count - 1];
                    lastNode.Value.Nodes.AddRange(nodes);
                    
                    // Update the nodes dictionary
                    foreach (var node in nodes)
                        lastNode.Value.NodesDict.Add(node.Key, node.Value);
                }
            }

            return levels[0];
        }
    }

    public class MiniYamlNode
    {
        public string Key { get; private set; }
        public MiniYaml Value { get; private set; }
        public string Location { get; private set; }

        public MiniYamlNode(string key, MiniYaml value, string location = null)
        {
            Key = key;
            Value = value;
            Location = location;
        }
    }
}
