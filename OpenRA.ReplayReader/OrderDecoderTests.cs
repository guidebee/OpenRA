using System;

namespace OpenRA.ReplayReader
{
    public class OrderDecoderTests
    {
        public static void RunTests()
        {
            Console.WriteLine("Running Order Decoder Tests...");

            // Test SetRallyPoint order decoding
            var setRallyPointTest = OrderDecoder.DecodeSetRallyPointExtraData("02-00-35-80-01-00");
            Console.WriteLine("\nSetRallyPoint Order Test:");
            Console.WriteLine("Input: 02-00-35-80-01-00");
            Console.WriteLine("Decoded Result:");
            Console.WriteLine(setRallyPointTest);

            // Add more test cases if needed

            Console.WriteLine("\nTests completed.");
        }
    }
}
