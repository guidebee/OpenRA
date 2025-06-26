using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenRA.ReplayReader
{
    public class OrderDecoderTests
    {
        public static void RunAllTests()
        {
            Console.WriteLine("Running OrderDecoder Tests...");
            
            // Run all test methods
            DecodeStartProduction_ValidData_ReturnsCount();
            DecodePauseProduction_Pause_ReturnsPauseAction();
            DecodePauseProduction_Resume_ReturnsResumeAction();
            DecodePlaceBuilding_ValidData_ReturnsFacingInfo();
            DecodeSetRallyPoint_ValidData_ReturnsCoordinates();
            DecodeChronoshift_ValidData_ReturnsSourceAndDestination();
            DecodeChat_TeamChat_ReturnsTeamNumber();
            DecodeChat_AllChat_ReturnsAllPlayers();
            DecodeIronCurtain_ValidData_ReturnsDuration();
            DecodeNukePower_ValidData_ReturnsTargetCell();
            DecodeGenericFlag_ValidData_ReturnsFlagValue();
            DecodeGenericExtraData_ValidData_ShowsRawData();
            DecodeOrderExtraData_EmptyData_ReturnsNoExtraData();
            
            Console.WriteLine("All tests completed!");
        }
        
        public static void DecodeStartProduction_ValidData_ReturnsCount()
        {
            // Arrange - ExtraData for producing 5 units
            var hexData = "05-00-00-00";
            
            // Act
            var result = OrderDecoder.DecodeOrderExtraData("StartProduction", hexData);
            
            // Assert
            Console.WriteLine("Test: DecodeStartProduction_ValidData_ReturnsCount");
            if (result.Contains("Count: 5"))
                Console.WriteLine("  PASSED");
            else
                Console.WriteLine($"  FAILED - Expected 'Count: 5' but got: {result}");
        }
        
        public static void DecodePauseProduction_Pause_ReturnsPauseAction()
        {
            // Arrange - ExtraData for pausing production (1)
            var hexData = "01-00-00-00";
            
            // Act
            var result = OrderDecoder.DecodeOrderExtraData("PauseProduction", hexData);
            
            // Assert
            Console.WriteLine("Test: DecodePauseProduction_Pause_ReturnsPauseAction");
            if (result.Contains("Action: Pause"))
                Console.WriteLine("  PASSED");
            else
                Console.WriteLine($"  FAILED - Expected 'Action: Pause' but got: {result}");
        }
        
        public static void DecodePauseProduction_Resume_ReturnsResumeAction()
        {
            // Arrange - ExtraData for resuming production (0)
            var hexData = "00-00-00-00";
            
            // Act
            var result = OrderDecoder.DecodeOrderExtraData("PauseProduction", hexData);
            
            // Assert
            Console.WriteLine("Test: DecodePauseProduction_Resume_ReturnsResumeAction");
            if (result.Contains("Action: Resume"))
                Console.WriteLine("  PASSED");
            else
                Console.WriteLine($"  FAILED - Expected 'Action: Resume' but got: {result}");
        }
        
        public static void DecodePlaceBuilding_ValidData_ReturnsFacingInfo()
        {
            // Arrange - ExtraData with facing direction
            var hexData = "02-00-00-00";
            
            // Act
            var result = OrderDecoder.DecodeOrderExtraData("PlaceBuilding", hexData);
            
            // Assert
            Console.WriteLine("Test: DecodePlaceBuilding_ValidData_ReturnsFacingInfo");
            if (result.Contains("Facing Direction: 2"))
                Console.WriteLine("  PASSED");
            else
                Console.WriteLine($"  FAILED - Expected 'Facing Direction: 2' but got: {result}");
        }
        
        public static void DecodeSetRallyPoint_ValidData_ReturnsCoordinates()
        {
            // Arrange - ExtraData with coordinates and subcell
            var hexData = "0A-00-14-00-01";
            
            // Act
            var result = OrderDecoder.DecodeOrderExtraData("SetRallyPoint", hexData);
            
            // Assert
            Console.WriteLine("Test: DecodeSetRallyPoint_ValidData_ReturnsCoordinates");
            if (result.Contains("Target Cell: (10, 20)") && result.Contains("SubCell: 1"))
                Console.WriteLine("  PASSED");
            else
                Console.WriteLine($"  FAILED - Expected coordinate and subcell info but got: {result}");
        }
        
        public static void DecodeChronoshift_ValidData_ReturnsSourceAndDestination()
        {
            // Arrange - ExtraData with source and destination coordinates
            var hexData = "0A-00-0B-00-14-00-15-00";
            
            // Act
            var result = OrderDecoder.DecodeOrderExtraData("Chronoshift", hexData);
            
            // Assert
            Console.WriteLine("Test: DecodeChronoshift_ValidData_ReturnsSourceAndDestination");
            if (result.Contains("Source: (10, 11)") && result.Contains("Destination: (20, 21)"))
                Console.WriteLine("  PASSED");
            else
                Console.WriteLine($"  FAILED - Expected source and destination but got: {result}");
        }
        
        public static void DecodeChat_TeamChat_ReturnsTeamNumber()
        {
            // Arrange - ExtraData with team number 2
            var hexData = "02-00-00-00";
            
            // Act
            var result = OrderDecoder.DecodeOrderExtraData("Chat", hexData);
            
            // Assert
            Console.WriteLine("Test: DecodeChat_TeamChat_ReturnsTeamNumber");
            if (result.Contains("Channel: Team 2"))
                Console.WriteLine("  PASSED");
            else
                Console.WriteLine($"  FAILED - Expected 'Channel: Team 2' but got: {result}");
        }
        
        public static void DecodeChat_AllChat_ReturnsAllPlayers()
        {
            // Arrange - ExtraData with team number 0 (all players)
            var hexData = "00-00-00-00";
            
            // Act
            var result = OrderDecoder.DecodeOrderExtraData("Chat", hexData);
            
            // Assert
            Console.WriteLine("Test: DecodeChat_AllChat_ReturnsAllPlayers");
            if (result.Contains("Channel: All Players"))
                Console.WriteLine("  PASSED");
            else
                Console.WriteLine($"  FAILED - Expected 'Channel: All Players' but got: {result}");
        }
        
        public static void DecodeIronCurtain_ValidData_ReturnsDuration()
        {
            // Arrange - ExtraData with duration
            var hexData = "C8-00-00-00";
            
            // Act
            var result = OrderDecoder.DecodeOrderExtraData("IronCurtain", hexData);
            
            // Assert
            Console.WriteLine("Test: DecodeIronCurtain_ValidData_ReturnsDuration");
            if (result.Contains("Duration: 200"))
                Console.WriteLine("  PASSED");
            else
                Console.WriteLine($"  FAILED - Expected 'Duration: 200' but got: {result}");
        }
        
        public static void DecodeNukePower_ValidData_ReturnsTargetCell()
        {
            // Arrange - ExtraData with target cell
            var hexData = "32-00-64-00";
            
            // Act
            var result = OrderDecoder.DecodeOrderExtraData("NukePower", hexData);
            
            // Assert
            Console.WriteLine("Test: DecodeNukePower_ValidData_ReturnsTargetCell");
            if (result.Contains("Target Cell: (50, 100)"))
                Console.WriteLine("  PASSED");
            else
                Console.WriteLine($"  FAILED - Expected 'Target Cell: (50, 100)' but got: {result}");
        }
        
        public static void DecodeGenericFlag_ValidData_ReturnsFlagValue()
        {
            // Arrange - ExtraData with a flag value
            var hexData = "01-00-00-00";
            
            // Act
            var result = OrderDecoder.DecodeOrderExtraData("Deploy", hexData);
            
            // Assert
            Console.WriteLine("Test: DecodeGenericFlag_ValidData_ReturnsFlagValue");
            if (result.Contains("Flag Value: 1"))
                Console.WriteLine("  PASSED");
            else
                Console.WriteLine($"  FAILED - Expected 'Flag Value: 1' but got: {result}");
        }
        
        public static void DecodeGenericExtraData_ValidData_ShowsRawData()
        {
            // Arrange - Some unknown ExtraData
            var hexData = "AA-BB-CC-DD";
            
            // Act
            var result = OrderDecoder.DecodeOrderExtraData("UnknownOrder", hexData);
            
            // Assert
            Console.WriteLine("Test: DecodeGenericExtraData_ValidData_ShowsRawData");
            if (result.Contains("Value:") && result.Contains("Raw Data:"))
                Console.WriteLine("  PASSED");
            else
                Console.WriteLine($"  FAILED - Expected value and raw data but got: {result}");
        }
        
        public static void DecodeOrderExtraData_EmptyData_ReturnsNoExtraData()
        {
            // Arrange
            var hexData = "";
            
            // Act
            var result = OrderDecoder.DecodeOrderExtraData("Any", hexData);
            
            // Assert
            Console.WriteLine("Test: DecodeOrderExtraData_EmptyData_ReturnsNoExtraData");
            if (result == "No extra data")
                Console.WriteLine("  PASSED");
            else
                Console.WriteLine($"  FAILED - Expected 'No extra data' but got: {result}");
        }
    }
}
