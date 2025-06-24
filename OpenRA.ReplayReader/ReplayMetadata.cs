using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OpenRA.ReplayReader
{
    public class ReplayMetadata
    {
        // Must be an invalid replay 'client' value
        public const int MetaStartMarker = -1;
        public const int MetaEndMarker = -2;
        public const int MetaVersion = 0x00000001;

        public readonly GameInformation GameInfo;
        public string FilePath { get; private set; }

        public ReplayMetadata(GameInformation info)
        {
            if (info == null)
                throw new ArgumentNullException(nameof(info));

            GameInfo = info;
        }

        ReplayMetadata(FileStream fs, string path)
        {
            FilePath = path;

            // Read start marker
            if (fs.ReadInt32() != MetaStartMarker)
                throw new InvalidOperationException("Expected MetaStartMarker but found an invalid value.");

            // Read version
            var version = fs.ReadInt32();
            if (version != MetaVersion)
                throw new NotSupportedException($"Metadata version {version} is not supported");

            // Read game info (max 100K limit as a safeguard against corrupted files)
            var data = fs.ReadLengthPrefixedString(Encoding.UTF8, 1024 * 100);
            GameInfo = GameInformation.Deserialize(data, path);
        }

        public static ReplayMetadata Read(string path)
        {
            try
            {
                Console.WriteLine($"Attempting to read replay file: {path}");
                Console.WriteLine($"File exists: {File.Exists(path)}");
                
                using (var fs = new FileStream(path, FileMode.Open))
                {
                    Console.WriteLine($"File opened. Length: {fs.Length} bytes");
                    
                    if (!fs.CanSeek)
                    {
                        Console.WriteLine("File cannot be seeked.");
                        return null;
                    }

                    if (fs.Length < 20)
                    {
                        Console.WriteLine("File is too small (less than 20 bytes).");
                        return null;
                    }

                    // Find the metadata marker
                    Console.WriteLine("Seeking to position near end of file to find metadata marker...");
                    fs.Seek(-12, SeekOrigin.End);
                    var position = fs.Position;
                    Console.WriteLine($"Current position: {position}");
                    
                    var metaEnd = fs.ReadInt32();
                    Console.WriteLine($"Read metaEnd marker: {metaEnd} (expected {MetaEndMarker})");

                    if (metaEnd != MetaEndMarker)
                    {
                        Console.WriteLine($"Invalid metaEnd marker: {metaEnd}. Not a valid replay or wrong format version.");
                        
                        // Debug: Dump the last few bytes to see what's there
                        fs.Seek(-20, SeekOrigin.End);
                        var lastBytes = new byte[20];
                        fs.Read(lastBytes, 0, 20);
                        Console.WriteLine("Last 20 bytes of file (hex):");
                        for (int i = 0; i < lastBytes.Length; i++)
                            Console.Write($"{lastBytes[i]:X2} ");
                        Console.WriteLine();
                        
                        return null;
                    }

                    var metaLength = fs.ReadInt32();
                    Console.WriteLine($"Read metadata length: {metaLength}");
                    
                    if (metaLength <= 0 || metaLength > fs.Length - 20)
                    {
                        Console.WriteLine($"Invalid metadata length: {metaLength}");
                        return null;
                    }
                    
                    fs.Seek(-metaLength - 12, SeekOrigin.End);
                    Console.WriteLine($"Seeking to metadata start position: {fs.Position}");

                    return new ReplayMetadata(fs, path);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading replay metadata: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                return null;
            }
        }
    }

    public class GameInformation
    {
        public string Mod;
        public string Version;
        public string MapUid;
        public string MapTitle;
        public DateTime StartTimeUtc;
        public DateTime EndTimeUtc;
        public int FinalGameTick;
        public bool IsSinglePlayer;
        public Player[] Players = Array.Empty<Player>();
        public HashSet<int> DisabledSpawnPoints;

        public TimeSpan Duration => EndTimeUtc - StartTimeUtc;

        public static GameInformation Deserialize(string data, string filename)
        {
            var info = new GameInformation();
            
            try
            {
                Console.WriteLine("Deserializing game information...");
                Console.WriteLine($"Data length: {data?.Length ?? 0} characters");
                
                // Simple mini-YAML parsing
                var lines = data.Split('\n');
                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();
                    if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith("#"))
                        continue;

                    var separatorIndex = trimmedLine.IndexOf(':');
                    if (separatorIndex < 0)
                        continue;

                    var key = trimmedLine.Substring(0, separatorIndex).Trim();
                    var value = trimmedLine.Substring(separatorIndex + 1).Trim();

                    // Remove surrounding quotes if present
                    if (value.StartsWith("\"") && value.EndsWith("\""))
                        value = value.Substring(1, value.Length - 2);

                    switch (key)
                    {
                        case "Mod":
                            info.Mod = value;
                            break;
                        case "Version":
                            info.Version = value;
                            break;
                        case "MapUid":
                            info.MapUid = value;
                            break;
                        case "MapTitle":
                            info.MapTitle = value;
                            break;
                        case "StartTimeUtc":
                            if (DateTime.TryParse(value, out var startTime))
                                info.StartTimeUtc = startTime;
                            break;
                        case "EndTimeUtc":
                            if (DateTime.TryParse(value, out var endTime))
                                info.EndTimeUtc = endTime;
                            break;
                        case "FinalGameTick":
                            if (int.TryParse(value, out var finalTick))
                                info.FinalGameTick = finalTick;
                            break;
                        case "IsSinglePlayer":
                            if (bool.TryParse(value, out var isSinglePlayer))
                                info.IsSinglePlayer = isSinglePlayer;
                            break;
                        case "Players":
                            // Player parsing would be done in a proper YAML parser
                            // For this simplified version, we'll parse players in a separate step
                            break;
                    }
                }

                // Simple player parsing
                var playerList = new List<Player>();
                var currentPlayer = new Player();
                bool inPlayerSection = false;

                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();
                    if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith("#"))
                        continue;

                    if (trimmedLine == "Players:")
                    {
                        inPlayerSection = true;
                        continue;
                    }
                    else if (inPlayerSection && trimmedLine.StartsWith("-"))
                    {
                        if (currentPlayer.Name != null) // Save previous player if exists
                            playerList.Add(currentPlayer);
                        
                        currentPlayer = new Player();
                        continue;
                    }
                    else if (!inPlayerSection || !trimmedLine.Contains(":"))
                    {
                        continue;
                    }

                    var separatorIndex = trimmedLine.IndexOf(':');
                    if (separatorIndex < 0)
                        continue;

                    var key = trimmedLine.Substring(0, separatorIndex).Trim();
                    var value = trimmedLine.Substring(separatorIndex + 1).Trim();

                    // Remove surrounding quotes if present
                    if (value.StartsWith("\"") && value.EndsWith("\""))
                        value = value.Substring(1, value.Length - 2);

                    switch (key)
                    {
                        case "Name":
                            currentPlayer.Name = value;
                            break;
                        case "FactionName":
                            currentPlayer.FactionName = value;
                            break;
                        case "FactionId":
                            currentPlayer.FactionId = value;
                            break;
                        case "Team":
                            if (int.TryParse(value, out var team))
                                currentPlayer.Team = team;
                            break;
                        case "IsBot":
                            if (bool.TryParse(value, out var isBot))
                                currentPlayer.IsBot = isBot;
                            break;
                        case "IsHuman":
                            if (bool.TryParse(value, out var isHuman))
                                currentPlayer.IsHuman = isHuman;
                            break;
                        case "Outcome":
                            if (Enum.TryParse<WinState>(value, out var outcome))
                                currentPlayer.Outcome = outcome;
                            break;
                    }
                }

                // Add the last player if exists
                if (currentPlayer.Name != null)
                    playerList.Add(currentPlayer);

                info.Players = playerList.ToArray();
                Console.WriteLine($"Parsed {info.Players.Length} players");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deserializing game information: {ex.Message}");
            }

            return info;
        }

        public string ResolvedPlayerName(Player player)
        {
            // In the real game, this would handle player aliases and other details
            return player.Name;
        }

        public class Player
        {
            public string Name;
            public string FactionName;
            public string FactionId;
            public int Team;
            public bool IsBot;
            public bool IsHuman;
            public WinState Outcome;
            public int SpawnPoint;
            public string Color;
        }
    }
}
