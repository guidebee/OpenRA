using System;
using System.Collections.Generic;
using System.IO;

namespace OpenRA.TemplateReader
{
    /// <summary>
    /// A simple implementation of MIX file reading for template extraction.
    /// </summary>
    public class MixFile
    {
        private readonly Dictionary<string, IndexEntry> index = new Dictionary<string, IndexEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly Stream stream;
        private readonly bool isOwner;

        public MixFile(Stream stream, bool isOwner = true)
        {
            this.stream = stream;
            this.isOwner = isOwner;

            try
            {
                // Read the MIX file header
                using (var reader = new BinaryReader(stream, System.Text.Encoding.ASCII, true))
                {
                    // Check if this is a valid MIX file with enough data
                    if (stream.Length < 10)
                    {
                        Console.WriteLine("Invalid MIX file: File is too small");
                        return;
                    }

                    // Store the original position
                    long originalPosition = stream.Position;

                    // Check for various MIX file formats
                    try
                    {
                        // Read the first 4 bytes to determine format
                        stream.Position = 0;
                        uint fileHeader = reader.ReadUInt32();

                        // Standard Red Alert MIX format
                        // Read the number of files
                        stream.Position = 4;
                        ushort numFiles = reader.ReadUInt16();

                        // Make sure we don't process too many files to prevent errors
                        numFiles = (ushort)Math.Min((int)numFiles, 10000); // Reasonable upper limit

                        uint headerSize = reader.ReadUInt32();

                        // Safety check for reasonable header size
                        if (headerSize > stream.Length || headerSize < 10)
                        {
                            Console.WriteLine($"Warning: Invalid header size {headerSize}, using default");
                            headerSize = 10; // Use a reasonable default
                        }

                        // Skip to the index section
                        stream.Position = headerSize;

                        // Read file entries
                        for (int i = 0; i < numFiles; i++)
                        {
                            // Check if we've reached the end of the file
                            if (stream.Position + 12 > stream.Length)
                                break;

                            uint id = reader.ReadUInt32();
                            uint offset = reader.ReadUInt32();
                            uint length = reader.ReadUInt32();

                            // Basic validation
                            if (offset > stream.Length || length > stream.Length || offset + length > stream.Length)
                                continue;

                            // Convert hash ID to filename (approximate, for common templates)
                            string filename = HashToFilename(id);

                            if (!string.IsNullOrEmpty(filename))
                            {
                                index[filename] = new IndexEntry { Offset = offset, Length = length };
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Warning during MIX parsing: {ex.Message}");
                        // Reset position and keep going
                        stream.Position = originalPosition;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading MIX file: {ex.Message}");
                if (isOwner)
                {
                    stream.Dispose();
                }
                throw;
            }
        }

        public bool Contains(string filename)
        {
            return index.ContainsKey(filename);
        }

        public byte[] Extract(string filename)
        {
            if (!index.TryGetValue(filename, out var entry))
                return null;

            lock (stream)
            {
                try
                {
                    // Validate that the offset and length are within the stream
                    if (entry.Offset >= stream.Length || entry.Length > stream.Length ||
                        entry.Offset + entry.Length > stream.Length)
                    {
                        Console.WriteLine($"Error: Invalid file entry for {filename}");
                        return null;
                    }

                    stream.Position = entry.Offset;
                    var data = new byte[entry.Length];
                    stream.Read(data, 0, (int)entry.Length);
                    return data;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error extracting {filename}: {ex.Message}");
                    return null;
                }
            }
        }

        public void Dispose()
        {
            if (isOwner)
                stream.Dispose();
        }

        private string HashToFilename(uint hash)
        {
            // Some known template files in Red Alert
            var knownHashes = new Dictionary<uint, string>()
            {
                // Temperate templates
                { 0x54454D50, "temperat.tem" }, // TEMP
                { 0x54303030, "t000.tem" },     // T000
                { 0x54303031, "t001.tem" },     // T001
                { 0x54303032, "t002.tem" },     // T002
                { 0x54303033, "t003.tem" },     // T003
                { 0x54303034, "t004.tem" },     // T004
                { 0x54303035, "t005.tem" },     // T005
                { 0x54303036, "t006.tem" },     // T006
                { 0x54303037, "t007.tem" },     // T007
                { 0x54303038, "t008.tem" },     // T008
                { 0x54303039, "t009.tem" },     // T009
                { 0x54303130, "t010.tem" },     // T010
                { 0x54303131, "t011.tem" },     // T011
                { 0x54303132, "t012.tem" },     // T012
                { 0x54303133, "t013.tem" },     // T013
                { 0x54303134, "t014.tem" },     // T014
                { 0x54303135, "t015.tem" },     // T015
                { 0x54303136, "t016.tem" },     // T016
                { 0x54303137, "t017.tem" },     // T017
                { 0x54303138, "t018.tem" },     // T018
                { 0x54303231, "t021.tem" },     // T021

                // Snow templates
                { 0x534E4F57, "snow.sno" },     // SNOW
                { 0x53303030, "s000.sno" },     // S000
                { 0x53303031, "s001.sno" },     // S001
                { 0x53303032, "s002.sno" },     // S002
                { 0x53303033, "s003.sno" },     // S003

                // Desert templates
                { 0x44455345, "desert.des" },   // DESE
                { 0x44303030, "d000.des" },     // D000
                { 0x44303031, "d001.des" },     // D001
                { 0x44303032, "d002.des" },     // D002

                // Interior templates
                { 0x494E5445, "interior.int" }, // INTE
                { 0x49303030, "i000.int" },     // I000
                { 0x49303031, "i001.int" },     // I001
            };

            if (knownHashes.TryGetValue(hash, out var filename))
                return filename;

            // Try to convert the hash to a template name
            // This is a simplified approach, not handling all cases
            byte[] bytes = BitConverter.GetBytes(hash);
            if (bytes.Length >= 4)
            {
                char t = (char)bytes[0];
                char d1 = (char)bytes[1];
                char d2 = (char)bytes[2];
                char d3 = (char)bytes[3];

                // Common template naming pattern: tNNN.tem, sNNN.sno, etc.
                if ((t == 't' || t == 'T') && char.IsDigit(d1) && char.IsDigit(d2) && char.IsDigit(d3))
                {
                    return $"{t}{d1}{d2}{d3}.tem";
                }
                if ((t == 's' || t == 'S') && char.IsDigit(d1) && char.IsDigit(d2) && char.IsDigit(d3))
                {
                    return $"{t}{d1}{d2}{d3}.sno";
                }
                if ((t == 'd' || t == 'D') && char.IsDigit(d1) && char.IsDigit(d2) && char.IsDigit(d3))
                {
                    return $"{t}{d1}{d2}{d3}.des";
                }
                if ((t == 'i' || t == 'I') && char.IsDigit(d1) && char.IsDigit(d2) && char.IsDigit(d3))
                {
                    return $"{t}{d1}{d2}{d3}.int";
                }
            }

            return null;
        }

        private class IndexEntry
        {
            public uint Offset;
            public uint Length;
        }
    }
}
