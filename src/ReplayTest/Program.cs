using FortniteReplayReader;
using Unreal.Core.Models.Enums; 
using Microsoft.Extensions.Logging;
using System.IO;

var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
var logger = loggerFactory.CreateLogger<ReplayReader>();
var replayPath = @"../../../downloaded_replays/33ac1cf5d549465da756c3bbe330c6bb.replay";

// Extract replay ID from filename
var replayId = Path.GetFileNameWithoutExtension(replayPath);
Console.WriteLine($"Processing replay: {replayId}");

var reader = new ReplayReader(logger, ParseMode.Full);

try 
{
    Console.WriteLine("Loading replay...");
    var replay = reader.ReadReplay(replayPath);
    
    Console.WriteLine($"Replay loaded: {replay.GameData.GameSessionId}");
    Console.WriteLine($"Players: {replay.PlayerData.Count()}");
    Console.WriteLine($"Eliminations: {replay.Eliminations?.Count() ?? 0}");
    
    // Check if any players have locations (indicates activity)
    var playersWithMovement = replay.PlayerData.Count(p => p.Locations?.Any() == true);
    Console.WriteLine($"Players with movement data: {playersWithMovement}");
    
    // Get damage summary from the reader's damage tracker
    var damageTracker = reader.GetDamageTracker();
    damageTracker.PrintSummary();
    
    // Export CSV with material and damage stats
    Console.WriteLine("\nExporting player stats to CSV...");
    var csvFileName = $"player_stats_{replayId}.csv";
    
    // Set Epic access token if you have one
    reader.SetEpicAccessToken("dbb29d4e04844d868240b66f5408090a");
    
    // Export the CSV
    await reader.ExportPlayerStatsToCSVAsync(csvFileName);
    
    // Also check the replay's built-in damage summary
    if (replay.DamageSummary != null)
    {
        Console.WriteLine($"\n--- Built-in Damage Summary ---");
        Console.WriteLine($"Total Players Tracked: {replay.DamageSummary.PlayerStats.Count}");
        Console.WriteLine($"Total Damage: {replay.DamageSummary.TotalDamage}");
        Console.WriteLine($"Total Shots Hit: {replay.DamageSummary.TotalShotsHit}");
        Console.WriteLine($"Total Eliminations: {replay.DamageSummary.TotalEliminations}");
        
        // Show top damage dealers
        Console.WriteLine("\nTop Damage Dealers:");
        foreach (var player in replay.DamageSummary.PlayerStats.OrderByDescending(p => p.TotalDamageDealt).Take(10))
        {
            Console.WriteLine($"  {player.PlayerId}: {player.TotalDamageDealt} damage, {player.Eliminations} eliminations");
        }
        
        // Show players who took most damage
        Console.WriteLine("\nMost Damage Taken:");
        foreach (var player in replay.DamageSummary.PlayerStats.OrderByDescending(p => p.TotalDamageTaken).Take(10))
        {
            Console.WriteLine($"  {player.PlayerId}: {player.TotalDamageTaken} damage taken");
        }
    }
    else
    {
        Console.WriteLine("No built-in damage summary found");
    }
    
    // Show material farming/building summary
    var playerStats = reader.GetPlayerRealtimeStats();
    if (playerStats.Any())
    {
        Console.WriteLine($"\n--- Material Activity Summary ---");
        var playersWithMaterials = playerStats.Where(p => 
            p.Value.ContainsKey("MaterialsFarmed") || p.Value.ContainsKey("BuildsPlaced"));
            
        foreach (var player in playersWithMaterials.Take(10))
        {
            var farmed = player.Value.ContainsKey("MaterialsFarmed") ? player.Value["MaterialsFarmed"] : 0;
            var built = player.Value.ContainsKey("BuildsPlaced") ? player.Value["BuildsPlaced"] : 0;
            Console.WriteLine($"  {player.Key}: {farmed} mats farmed, {built} mats used for builds");
        }
    }
    
    // Show detailed damage events if available
    var detailedStats = damageTracker.GetDamageSummary();
    if (detailedStats.PlayerStats.Any())
    {
        Console.WriteLine($"\n--- Detailed Damage Events ---");
        foreach (var player in detailedStats.PlayerStats.Where(p => p.DamageDealt.Any()))
        {
            Console.WriteLine($"\n{player.PlayerId} damage events:");
            foreach (var dmg in player.DamageDealt.Take(5)) // Show first 5 events
            {
                Console.WriteLine($"  -> {dmg.Amount} to {dmg.VictimId ?? "Unknown"} {(dmg.IsFatal ? "(ELIMINATION)" : "")}");
            }
            if (player.DamageDealt.Count > 5)
            {
                Console.WriteLine($"  ... and {player.DamageDealt.Count - 5} more");
            }
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    Console.WriteLine($"Stack trace: {ex.StackTrace}");
}

Console.WriteLine("\nPress any key to exit...");
Console.ReadKey();