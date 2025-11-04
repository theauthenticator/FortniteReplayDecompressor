using FortniteReplayReader;
using Unreal.Core.Models.Enums;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;


// === CONFIGURATION ===
var replayDirectory = @"../../../downloaded_replays";
var outputDirectory = @"../../../processed_csvs";
var epicAccessToken = "dbb29d4e04844d868240b66f5408090a";

var replayIds = new[]
{
    "99213d64337b43ee918c5bfaa0f8a1e0",
    "dd2b5a2d74b5414ba4f228cc9463c03f",
    "899a1b1da214499b9584f16f7896fb4f",
    "a877d042840648a3bda24cb9b831459b",
};

var replayIds_Singular = new[]
{
    "a877d042840648a3bda24cb9b831459b",
};

var maxParallel = 4;

Directory.CreateDirectory(outputDirectory);
// === LOGGER SETUP ===
bool enableLogging = true;

ILogger<ReplayReader> logger;

if (enableLogging)
{
    var loggerFactory = LoggerFactory.Create(builder =>
    {
        builder.AddConsole().SetMinimumLevel(LogLevel.Information);
    });
    logger = loggerFactory.CreateLogger<ReplayReader>();
}
else
{
    logger = NullLogger<ReplayReader>.Instance;
}

Console.WriteLine($"Preparing to process {replayIds.Length} replay(s)...\n");

// === PROCESS EACH REPLAY ===
await Parallel.ForEachAsync(replayIds_Singular, new ParallelOptions { MaxDegreeOfParallelism = maxParallel }, async (replayId, token) =>
{
    var replayPath = Path.Combine(replayDirectory, $"{replayId}.replay");
    var csvFilePath = Path.Combine(outputDirectory, $"player_stats_{replayId}.csv");

    if (!File.Exists(replayPath))
    {
        Console.WriteLine($"❌ Replay file not found: {replayPath}");
        return;
    }

    if (File.Exists(csvFilePath))
    {
        Console.WriteLine($"⚠️ Overwriting existing CSV for {replayId}...");
        try
        {
            File.Delete(csvFilePath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Failed to delete old CSV: {ex.Message}");
            return;
        }
    }


    Console.WriteLine($"\n==== Processing replay: {replayId} ====\n");

    var reader = new ReplayReader(logger, ParseMode.Full);
    reader.SetEpicAccessToken(epicAccessToken);

    try
    {
        Console.WriteLine("Loading replay...");
        var replay = reader.ReadReplay(replayPath);
        Console.WriteLine($"Replay loaded: {replay.GameData.GameSessionId}");

        Console.WriteLine($"Players: {replay.PlayerData.Count()}");
        Console.WriteLine($"Eliminations: {replay.Eliminations?.Count() ?? 0}");


        Console.WriteLine($"Players: {replay.PlayerData.Count()}");
        Console.WriteLine($"Eliminations: {replay.Eliminations?.Count() ?? 0}");

        // ✅ LOG KILLS FROM PLAYERDATA
        Console.WriteLine("\n📊 DEBUG PLAYER KILL SUMMARY:");
        Console.WriteLine(new string('=', 80));
        var playersWithKills = replay.PlayerData
            .Where(p => p.Kills.HasValue && p.Kills > 0)
            .OrderByDescending(p => p.Kills)
            .ToList();

        if (playersWithKills.Any())
        {
            int rank = 1;
            foreach (var player in playersWithKills)
            {
                var displayName = player.PlayerName ?? player.EpicId ?? $"Bot_{player.BotId}";
                Console.WriteLine($"{rank}. {displayName}");
                Console.WriteLine($"   └─ Kills: {player.Kills} | Team: {player.TeamIndex} | Placement: {player.Placement}");
                rank++;
            }
        }
        else
        {
            Console.WriteLine("No players with kills found.");
        }
        Console.WriteLine(new string('=', 80));



        var damageTracker = reader.GetDamageTracker();
        damageTracker.PrintSummary();

        Console.WriteLine("Exporting player stats to CSV...");
        await reader.ExportPlayerStatsToCSVAsync(csvFilePath);
        Console.WriteLine($"✅ Exported to {csvFilePath}");

        if (replay.DamageSummary != null)
        {
            Console.WriteLine("--- Built-in Damage Summary ---");
            Console.WriteLine($"Total Damage: {replay.DamageSummary.TotalDamage}");
            Console.WriteLine($"Total Eliminations: {replay.DamageSummary.TotalEliminations}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Error processing {replayId}: {ex.Message}\n{ex.StackTrace}");

    }
});

Console.WriteLine("\nAll replay IDs processed. Press any key to exit...");
Console.ReadKey();
