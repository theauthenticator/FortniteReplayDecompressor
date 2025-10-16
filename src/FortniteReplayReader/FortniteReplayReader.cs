using FortniteReplayReader.Exceptions;
using FortniteReplayReader.Extensions;
using FortniteReplayReader.Models;
using FortniteReplayReader.Models.Enums;
using FortniteReplayReader.Models.Events;
using FortniteReplayReader.Models.NetFieldExports;
using FortniteReplayReader.Models.NetFieldExports.Weapons;
using Microsoft.Extensions.Logging;
using FortniteReplayReader.Models.Damage;
using FortniteReplayReader.Models.NetFieldExports.RPC;
using System.Collections.Generic;
using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Reflection;
using Unreal.Core.Attributes;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Unreal.Core;
using Unreal.Core.Contracts;
using Unreal.Core.Exceptions;
using Unreal.Core.Models;
using Unreal.Core.Models.Enums;
using Unreal.Encryption;
using System.Collections;
using FortniteReplayReader.Models.NetFieldExports.Buildings;
using FortniteReplayReader.Models.NetFieldExports.Builds;
using FortniteReplayReader.Models.NetFieldExports.Vehicles;

namespace FortniteReplayReader;


public class ReplayReader : Unreal.Core.ReplayReader<FortniteReplay>
{

    // Harvest hit tracking - add this with your other private fields
    private class HarvestHit
    {
        public string PlayerId { get; set; }
        public uint ObjectGuid { get; set; }
        public string MaterialType { get; set; }
        public DateTime Timestamp { get; set; }
    }

    private Queue<HarvestHit> recentHarvestHits = new Queue<HarvestHit>();

    private bool _verboseLogging = false; // Set to false to suppress most logs


    private void LogVerbose(string message)
    {
        if (_verboseLogging)
        {
            Console.WriteLine(message);
        }
    }



    private Dictionary<uint, FortBroadcastRemoteClientInfo> lastBroadcastInfo = new();

    private Dictionary<uint, uint> broadcastChannelOwner = new();
    private Dictionary<uint, string> archetypeGuidToPath = new();

    private Dictionary<uint, uint> actorGuidToArchetypeGuid = new();

    private HashSet<string> allExportTypes = new HashSet<string>();
    private int fortBroadcastCallCount = 0;
    private BuildTracker BuildTracker = new();
    private Dictionary<uint, string> actorGuidToPlayerId = new Dictionary<uint, string>();
    private Dictionary<string, PlayerInfo> playerInfoByPlayerId = new Dictionary<string, PlayerInfo>();
    private DamageTracker DamageTracker = new();
    private BuildOwnershipTracker _buildOwnershipTracker = new();

    private BuildAttributionTracker _buildAttributionTracker = new();
    private Dictionary<string, string> playerNames = new Dictionary<string, string>();
    private FortniteReplayBuilder Builder;

    private Dictionary<short, string> persistentIdToPlayerId = new(); // WorldPlayerId -> Internal PlayerId
    private Dictionary<string, string> playerIdToEpicId = new(); // Internal PlayerId -> Epic Account ID

    // Channel and export analysis
    private Dictionary<uint, string> channelToPlayerId = new Dictionary<uint, string>();
    private Dictionary<uint, string> channelTypes = new Dictionary<uint, string>();
    private Dictionary<string, List<uint>> exportTypeToChannels = new Dictionary<string, List<uint>>();
    private Dictionary<uint, uint> channelToActorGuid = new Dictionary<uint, uint>();
    private Dictionary<uint, List<string>> channelExportHistory = new Dictionary<uint, List<string>>();

    private Dictionary<string, HashSet<string>> statPropertiesByType = new Dictionary<string, HashSet<string>>();

    private HarvestTracker HarvestTracker = new();

    // Stat tracking
    private Dictionary<string, List<object>> allObservedStats = new Dictionary<string, List<object>>();
    private HashSet<string> unknownExportTypes = new HashSet<string>();
    private Dictionary<string, Dictionary<string, object>> playerRealtimeStats = new Dictionary<string, Dictionary<string, object>>();
    private Dictionary<uint, Dictionary<string, object>> channelStats = new Dictionary<uint, Dictionary<string, object>>();
    private List<Stats> AllPlayerStats = new List<Stats>();

    private Dictionary<string, List<(string Group, string Metadata, int Size, uint Time)>> uniqueEvents = new Dictionary<string, List<(string, string, int, uint)>>();


    private static readonly HttpClient httpClient = new HttpClient();
    private string? epicAccessToken = null;

    private HarvestableDatabase harvestableDatabase = new HarvestableDatabase();
    private Dictionary<string, UnknownHarvestableInfo> unknownHarvestables = new Dictionary<string, UnknownHarvestableInfo>(StringComparer.OrdinalIgnoreCase);

    // Helper class for tracking unknowns
    private class UnknownHarvestableInfo
    {
        public string BlueprintClass { get; set; }
        public string ArchetypePath { get; set; }
        public int HitCount { get; set; }
        public string EstimatedMaterialType { get; set; }
        public HashSet<string> HitByPlayers { get; set; } = new HashSet<string>();
        public List<float> HitTimestamps { get; set; } = new List<float>();
    }

    private string ExtractBlueprintClass(string actorName)
    {
        if (string.IsNullOrEmpty(actorName)) return actorName;

        // BP_Hermes_Birch_Tree_B_B_C_UAID_... → BP_Hermes_Birch_Tree_B_B_C
        var uaidIndex = actorName.IndexOf("_UAID_");
        if (uaidIndex > 0)
        {
            return actorName.Substring(0, uaidIndex);
        }
        return actorName;
    }

    private void TrackUnknownHarvestable(string actorName, string archetypePath, string playerId, float gameTime)
    {
        var blueprintClass = ExtractBlueprintClass(actorName);

        if (string.IsNullOrEmpty(blueprintClass)) return;

        // Check if we know about this harvestable
        if (harvestableDatabase.Contains(blueprintClass))
        {
            return; // Known harvestable, no need to track
        }

        // Unknown harvestable - track it
        if (!unknownHarvestables.ContainsKey(blueprintClass))
        {
            unknownHarvestables[blueprintClass] = new UnknownHarvestableInfo
            {
                BlueprintClass = blueprintClass,
                ArchetypePath = archetypePath,
                HitCount = 0,
                EstimatedMaterialType = GuessMaterialType(blueprintClass)
            };
        }

        var info = unknownHarvestables[blueprintClass];
        info.HitCount++;
        info.HitByPlayers.Add(playerId);
        info.HitTimestamps.Add(gameTime);
    }

    private string GuessMaterialType(string blueprintClass)
    {
        if (string.IsNullOrEmpty(blueprintClass)) return "Unknown";

        var lower = blueprintClass.ToLowerInvariant();

        // Trees (Wood)
        if (lower.Contains("tree") || lower.Contains("birch") ||
            lower.Contains("oak") || lower.Contains("pine") ||
            lower.Contains("palm") || lower.Contains("wood") ||
            lower.Contains("forest") || lower.Contains("jungle") ||
            lower.Contains("maple") || lower.Contains("cedar"))
            return "Wood";

        // Rocks (Stone)
        if (lower.Contains("rock") || lower.Contains("stone") ||
            lower.Contains("boulder") || lower.Contains("cliff") ||
            lower.Contains("granite") || lower.Contains("rubble"))
            return "Stone";

        // Metal sources
        if (lower.Contains("car") || lower.Contains("vehicle") ||
            lower.Contains("truck") || lower.Contains("metal") ||
            lower.Contains("fence") || lower.Contains("container") ||
            lower.Contains("barrel") || lower.Contains("dumpster") ||
            lower.Contains("trailer") || lower.Contains("van"))
            return "Metal";

        // Buildings (could be any material)
        if (lower.Contains("wall") || lower.Contains("floor") ||
            lower.Contains("roof") || lower.Contains("stair") ||
            lower.Contains("building"))
            return "Building";

        return "Unknown";
    }

    public void ExportUnknownHarvestables(string outputPath)
    {
        if (unknownHarvestables.Count == 0)
        {
            Console.WriteLine("\n✅ All harvestables in this replay are known!");
            return;
        }

        Console.WriteLine("\n=== UNKNOWN HARVESTABLES REPORT ===");
        Console.WriteLine($"Found {unknownHarvestables.Count} unknown harvestable types\n");

        var sorted = unknownHarvestables.Values
            .OrderByDescending(x => x.HitCount)
            .ToList();

        var report = new List<string>();
        report.Add("BlueprintClass,HitCount,UniquePlayersHit,EstimatedMaterialType,ArchetypePath,FirstHitTime,LastHitTime");

        Console.WriteLine("Top Unknown Harvestables:");
        Console.WriteLine("─────────────────────────────────────────────────────────");

        foreach (var unknown in sorted.Take(20))
        {
            var firstHit = unknown.HitTimestamps.Min();
            var lastHit = unknown.HitTimestamps.Max();

            Console.WriteLine($"\n{unknown.BlueprintClass}");
            Console.WriteLine($"  Hit Count: {unknown.HitCount}");
            Console.WriteLine($"  Unique Players: {unknown.HitByPlayers.Count}");
            Console.WriteLine($"  Estimated Type: {unknown.EstimatedMaterialType}");
            Console.WriteLine($"  First Hit: {firstHit:F1}s, Last Hit: {lastHit:F1}s");

            report.Add($"\"{unknown.BlueprintClass}\",{unknown.HitCount},{unknown.HitByPlayers.Count},\"{unknown.EstimatedMaterialType}\",\"{unknown.ArchetypePath}\",{firstHit:F1},{lastHit:F1}");
        }

        File.WriteAllLines(outputPath, report);
        Console.WriteLine($"\n✅ Unknown harvestables exported to: {outputPath}");
        Console.WriteLine($"   Total unknown types: {unknownHarvestables.Count}");
        Console.WriteLine($"   Total hits on unknowns: {unknownHarvestables.Values.Sum(x => x.HitCount)}");
    }


    public void SetEpicAccessToken(string accessToken)
    {
        epicAccessToken = accessToken;
    }

    private async Task<string> GetDisplayNameFromEpicAPI(string accountId)
    {
        if (string.IsNullOrEmpty(epicAccessToken))
        {
            return "Unknown";
        }

        try
        {
            // Convert to lowercase for Epic API
            var lowercaseAccountId = accountId.ToLower();
            var url = $"https://account-public-service-prod.ol.epicgames.com/account/api/public/account/{lowercaseAccountId}";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Authorization", $"Bearer {epicAccessToken}");

            var response = await httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var accountData = JsonSerializer.Deserialize<JsonElement>(json);

                if (accountData.TryGetProperty("displayName", out var displayNameElement))
                {
                    return displayNameElement.GetString() ?? accountId;
                }
            }
            else
            {
                LogVerbose($"API error for {accountId}: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            LogVerbose($"Error fetching display name for {accountId}: {ex.Message}");
        }

        return accountId; // Fallback to account ID
    }

    public ReplayReader(ILogger? logger = null, ParseMode parseMode = ParseMode.Debug) : base(logger, parseMode)
    {
        Builder = new FortniteReplayBuilder();
        DamageTracker = new DamageTracker();
        BuildTracker = new BuildTracker();

        var databasePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FortniteHarvestables_v2.json");
        harvestableDatabase.Load(databasePath);
        harvestableDatabase.PrintStatistics();

        LogVerbose("=== SYSTEMATIC REPLAY STRUCTURE ANALYSIS ===");
        LogVerbose("Phase 1: Channel mapping and export type distribution");
        LogVerbose("Phase 2: Actor-to-player relationship tracking");
        LogVerbose("Phase 3: Stat data location identification");
        LogVerbose("Phase 4: DEEP MATERIAL SCANNING ENABLED");
        LogVerbose("==============================================");
    }

    public FortniteReplay ReadReplay(string fileName)
    {
        using var stream = File.Open(fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return ReadReplay(stream);
    }

    public FortniteReplay ReadReplay(Stream stream)
    {
        using var archive = new Unreal.Core.BinaryReader(stream);

        Builder = new FortniteReplayBuilder();
        DamageTracker = new DamageTracker();
        ReadReplay(archive);

        var replay = Builder.Build(Replay);
        replay.DamageSummary = DamageTracker.GetDamageSummary();

        // Print comprehensive analysis
        PrintStructuralAnalysis();
        PrintEventSummary();
        BuildTracker.PrintBuildSummary();
        BuildTracker.PrintSanityChecks();


        var unknownHarvestablesPath = "unknown_harvestables.csv";
        ExportUnknownHarvestables(unknownHarvestablesPath);

        return replay;
    }

    private string _branch;
    public int Major { get; set; }
    public int Minor { get; set; }
    public string Branch
    {
        get => _branch;
        set
        {
            var regex = new Regex(@"\+\+Fortnite\+Release\-(?<major>\d+)\.(?<minor>\d*)");
            var result = regex.Match(value);
            if (result.Success)
            {
                Major = int.Parse(result.Groups["major"]?.Value ?? "0");
                Minor = int.Parse(result.Groups["minor"]?.Value ?? "0");
            }
            _branch = value;
        }
    }


    private void PrintUnknownExportsSummary()
    {
        LogVerbose("\n=== ALL FORT CLIENT OBSERVED STATS SUMMARY ===");
        LogVerbose($"Found {allObservedStats.Count} unique stat types:");

        foreach (var stat in allObservedStats.OrderBy(x => x.Key))
        {
            var values = stat.Value.Take(5).Select(v => v.ToString()).ToList();
            var valueRange = stat.Value.Count > 5 ? $" (showing 5 of {stat.Value.Count})" : "";
            LogVerbose($"  {stat.Key}: {string.Join(", ", values)}{valueRange}");
        }

        LogVerbose($"\n=== ALL UNKNOWN EXPORT TYPES ===");
        foreach (var exportType in unknownExportTypes.OrderBy(x => x))
        {
            LogVerbose($"  - {exportType}");
        }
    }

    protected override void OnChannelOpened(uint channelIndex, NetworkGUID? actorGuid)
    {
        base.OnChannelOpened(channelIndex, actorGuid);

        if (actorGuid != null && Channels[channelIndex]?.Actor != null)
        {
            var actor = Channels[channelIndex].Actor;

            if (actor.Archetype != null)
            {
                // Only ONE .Value - they're already uint!
                var actorGuidValue = actorGuid.Value;  // NetworkGUID? -> uint (not NetworkGUID!)
                var archetypeGuidValue = actor.Archetype.Value;  // NetworkGUID? -> uint

                actorGuidToArchetypeGuid[actorGuidValue] = archetypeGuidValue;

                if (_netGuidCache.TryGetPathName(archetypeGuidValue, out var archetypePath))
                {
                    archetypeGuidToPath[archetypeGuidValue] = archetypePath;
                    Console.WriteLine($"🌲 Actor spawned: ActorGUID={actorGuidValue}, Archetype={archetypePath}");
                }
                else
                {
                    Console.WriteLine($"🌲 Actor spawned: ActorGUID={actorGuidValue}, ArchetypeGUID={archetypeGuidValue} (path pending)");
                }
            }
        }
    }
    private float GetCurrentGameTime()
    {
        return (float) (Replay.Info?.LengthInMs ?? 0) / 1000.0f;
    }

    private string DetermineChannelType(uint channelIndex, uint actorGuid)
    {
        if (actorGuid == 1)
        {
            return "GAMESTATE";
        }

        return "UNKNOWN";
    }

    protected override void OnChannelClosed(uint channelIndex, NetworkGUID? actor)
    {
        if (actor != null)
        {
            Builder.RemoveChannel(channelIndex);
            LogVerbose($"Channel {channelIndex} CLOSED (Actor: {actor.Value})");
        }
    }

    protected override void OnExternalDataRead(uint channelIndex, IExternalData? externalData)
    {
        if (externalData != null)
        {
            Console.WriteLine($"\n🔔 EXTERNAL DATA on channel {channelIndex}");
            Console.WriteLine($"   Archive Type: {externalData.Archive.GetType().Name}");

            // Get the actor on this channel to identify the player
            var actorGuid = channelToActorGuid.GetValueOrDefault(channelIndex);
            var playerId = GetPlayerIdFromActor(actorGuid);
            Console.WriteLine($"   Player: {playerId}");

            // Existing player name data handling
            var playerNameData = new PlayerNameData(externalData.Archive);
            Builder.UpdatePrivateName(channelIndex, playerNameData);
            LogVerbose($"External Data on Channel {channelIndex}");
        }
    }

    private string GetMaterialTypeFromArchetype(string archetype)
    {
        var lowerPath = archetype.ToLowerInvariant();

        // Trees
        if (lowerPath.Contains("tree") ||
            lowerPath.Contains("birch") ||
            lowerPath.Contains("oak") ||
            lowerPath.Contains("pine") ||
            lowerPath.Contains("cherryblossom"))
            return "Wood";

        // Rocks/Stone
        if (lowerPath.Contains("rock") ||
            lowerPath.Contains("stone") ||
            lowerPath.Contains("boulder") ||
            lowerPath.Contains("rubble"))
            return "Stone";

        // Metal sources
        if (lowerPath.Contains("car") ||
            lowerPath.Contains("vehicle") ||
            lowerPath.Contains("truck") ||
            lowerPath.Contains("metal") ||
            lowerPath.Contains("container"))
            return "Metal";

        // Buildings get materials based on type
        if (lowerPath.Contains("wall") ||
            lowerPath.Contains("floor") ||
            lowerPath.Contains("roof") ||
            lowerPath.Contains("stair"))
            return "Building";

        return "Unknown";
    }


    protected override void OnNetDeltaRead(uint channelIndex, NetDeltaUpdate update)
    {
        var exportTypeName = update.Export?.GetType().Name ?? "NULL";
        LogVerbose($"NET DELTA: {exportTypeName} on Channel {channelIndex}");

        TrackExportOnChannel(channelIndex, exportTypeName);

        // Scan ALL exports for materials
        if (update.Export != null)
        {
        }

        switch (update.Export)
        {
            case ActiveGameplayModifier modifier:
                Builder.UpdateGameplayModifiers(modifier);
                break;
            case SpawnMachineRepData spawnMachine:
                Builder.UpdateRebootVan(channelIndex, spawnMachine);
                break;
            case FortInventory inventory:
                ProcessInventoryData(inventory, channelIndex, "DELTA");
                Builder.UpdateInventory(channelIndex, inventory);
                break;
            case FortClientObservedStat clientStat:
                ProcessClientStat(clientStat, channelIndex);
                break;
            case PlayerDamagedResourceBuilding harvest:
                Console.WriteLine($"\n🪓 HARVEST EVENT DETECTED!");
                Console.WriteLine($"  BuildingSMActor: {harvest.BuildingSMActor}");
                Console.WriteLine($"  ResourceType: {harvest.PotentialResourceType}");
                Console.WriteLine($"  ResourceCount: {harvest.PotentialResourceCount}");
                Console.WriteLine($"  Destroyed: {harvest.bDestroyed}");
                Console.WriteLine($"  Hit Weakspot: {harvest.bJustHitWeakspot}");
                Console.WriteLine($"  Channel: {channelIndex}");

                // Get player ID from channel
                var actorGuid = channelToActorGuid.GetValueOrDefault(channelIndex);
                var playerId = GetPlayerIdFromActor(actorGuid);

                Console.WriteLine($"  Player: {playerId}");

                // Determine material type
                string materialType = harvest.PotentialResourceType switch
                {
                    0 => "Wood",
                    1 => "Stone",
                    2 => "Metal",
                    _ => "Unknown"
                };

                // Record the harvest
                HarvestTracker.RecordDirectHarvest(
                    playerId,
                    materialType,
                    harvest.PotentialResourceCount,
                     harvest.bJustHitWeakspot == true,
                    (float) Builder.GetCurrentTimeDouble()
                );

                Console.WriteLine($"✓ Recorded: {playerId} harvested {harvest.PotentialResourceCount} {materialType}");
                break;

            default:
                AnalyzeUnknownExport(update.Export, channelIndex, "DELTA");
                break;
        }
    }

    private string GetLootFromArchetype(string archetype)
    {
        if (archetype.Contains("Tree", StringComparison.OrdinalIgnoreCase))
            return "Wood (varies by size)";
        if (archetype.Contains("Rock", StringComparison.OrdinalIgnoreCase) ||
            archetype.Contains("Stone", StringComparison.OrdinalIgnoreCase))
            return "Stone (varies by size)";
        if (archetype.Contains("Car", StringComparison.OrdinalIgnoreCase) ||
            archetype.Contains("Vehicle", StringComparison.OrdinalIgnoreCase))
            return "Metal (~40-50)";
        if (archetype.Contains("Metal", StringComparison.OrdinalIgnoreCase))
            return "Metal (varies)";

        return "Unknown loot type";
    }

    private void TrackExportOnChannel(uint channelIndex, string exportType)
    {
        if (!exportTypeToChannels.ContainsKey(exportType))
        {
            exportTypeToChannels[exportType] = new List<uint>();
        }
        if (!exportTypeToChannels[exportType].Contains(channelIndex))
        {
            exportTypeToChannels[exportType].Add(channelIndex);
        }

        if (!channelExportHistory.ContainsKey(channelIndex))
        {
            channelExportHistory[channelIndex] = new List<string>();
        }
        channelExportHistory[channelIndex].Add(exportType);
    }

    private Dictionary<string, float> _lastInventoryUpdateTime = new();

    private void ProcessInventoryData(FortInventory inventory, uint channelIndex, string context)
    {
        LogVerbose($"=== {context} INVENTORY on Channel {channelIndex} ===");
        LogVerbose($"ReplayPawn: {inventory.ReplayPawn}");
        LogVerbose($"Count: {inventory.Count}, LoadedAmmo: {inventory.LoadedAmmo}");
        LogVerbose($"A: {inventory.A}, B: {inventory.B}, C: {inventory.C}, D: {inventory.D}");

        string? playerId = null;

        // Method 1: Use ReplayPawn if available
        if (inventory.ReplayPawn.HasValue && actorGuidToPlayerId.TryGetValue(inventory.ReplayPawn.Value, out playerId))
        {
            LogVerbose($"✓ Inventory belongs to player via ReplayPawn: {playerId}");
            channelToPlayerId[channelIndex] = playerId;
        }
        // Method 2: Use stored channel association (for DELTA updates)
        else if (channelToPlayerId.ContainsKey(channelIndex))
        {
            playerId = channelToPlayerId[channelIndex];
            LogVerbose($"✓ Using stored channel association: Channel {channelIndex} -> {playerId}");
        }
        // Method 3: Try to associate via channel patterns
        else
        {
            playerId = TryAssociateInventoryWithPlayer(channelIndex);
            if (!string.IsNullOrEmpty(playerId))
            {
                LogVerbose($"✓ Associated via pattern matching: {playerId}");
            }
        }

        if (!string.IsNullOrEmpty(playerId))
        {
            if (!playerRealtimeStats.ContainsKey(playerId))
            {
                playerRealtimeStats[playerId] = new Dictionary<string, object>();
            }

            // FREQUENCY TRACKING
            var currentTime = (float) Builder.GetCurrentTimeDouble();

            if (_lastInventoryUpdateTime.ContainsKey(playerId))
            {
                var timeSinceLastUpdate = currentTime - _lastInventoryUpdateTime[playerId];
                LogVerbose($"⏱️ Inventory update interval for {playerId}: {timeSinceLastUpdate:F3}s");
            }
            else
            {
                LogVerbose($"⏱️ First inventory update for {playerId}");
            }

            _lastInventoryUpdateTime[playerId] = currentTime;

            // MATERIAL TRACKING
            var wood = inventory.A.HasValue ? (uint) inventory.A.Value : (uint?) null;
            var stone = inventory.B.HasValue ? (uint) inventory.B.Value : (uint?) null;
            var metal = inventory.C.HasValue ? (uint) inventory.C.Value : (uint?) null;

            LogVerbose($"📦 ATTEMPTING MATERIAL TRACKING for {playerId}:");
            LogVerbose($"   Wood={wood}, Stone={stone}, Metal={metal}");

            // Store raw values in playerRealtimeStats
            if (inventory.A.HasValue) playerRealtimeStats[playerId]["Inv_A"] = inventory.A.Value;
            if (inventory.B.HasValue) playerRealtimeStats[playerId]["Inv_B"] = inventory.B.Value;
            if (inventory.C.HasValue) playerRealtimeStats[playerId]["Inv_C"] = inventory.C.Value;
            if (inventory.D.HasValue) playerRealtimeStats[playerId]["Inv_D"] = inventory.D.Value;

            var playerLoc = GetPlayerLocation(playerId);

            if (playerLoc != null)
            {
                LogVerbose($"   Player location: {playerLoc}");
            }
            else
            {
                LogVerbose($"   ⚠️ No player location available");
            }

            _buildAttributionTracker.UpdatePlayerMaterials(
                playerId,
                wood,
                stone,
                metal,
                currentTime,
                playerLoc
            );

            LogVerbose($"✓ Materials tracked for {playerId}");
        }
        else
        {
            LogVerbose($"❌ Could not associate inventory with player");

            if (!channelStats.ContainsKey(channelIndex))
            {
                channelStats[channelIndex] = new Dictionary<string, object>();
            }

            if (inventory.A.HasValue) channelStats[channelIndex]["Inv_A"] = inventory.A.Value;
            if (inventory.B.HasValue) channelStats[channelIndex]["Inv_B"] = inventory.B.Value;
            if (inventory.C.HasValue) channelStats[channelIndex]["Inv_C"] = inventory.C.Value;
            if (inventory.D.HasValue) channelStats[channelIndex]["Inv_D"] = inventory.D.Value;
        }
    }

    private string? TryAssociateInventoryWithPlayer(uint inventoryChannelIndex)
    {
        // For high-numbered inventory channels (3000+), try different strategies

        // Strategy 1: Look for the closest player state channel
        var playerStateChannels = exportTypeToChannels.GetValueOrDefault("FortPlayerState", new List<uint>());
        var closestPlayerChannel = playerStateChannels.OrderBy(x => Math.Abs((int) x - (int) inventoryChannelIndex)).FirstOrDefault();

        if (closestPlayerChannel > 0)
        {
            var playerId = Builder.GetPlayerIdFromChannel(closestPlayerChannel);
            if (!string.IsNullOrEmpty(playerId))
            {
                LogVerbose($"Inventory channel {inventoryChannelIndex} associated with closest player channel {closestPlayerChannel} -> {playerId}");
                return playerId;
            }
        }

        // Strategy 2: For numbered patterns, try modulo operations
        if (inventoryChannelIndex > 3000)
        {
            var playerList = actorGuidToPlayerId.Values.ToList();
            if (playerList.Count > 0)
            {
                var playerIndex = (int) (inventoryChannelIndex % playerList.Count);
                var mappedPlayer = playerList[playerIndex];
                LogVerbose($"Pattern-based inventory association: Channel {inventoryChannelIndex} -> Player index {playerIndex} -> {mappedPlayer}");
                return mappedPlayer;
            }
        }

        // Strategy 3: Time-based association
        if (playerInfoByPlayerId.Count > 0)
        {
            var lastPlayer = playerInfoByPlayerId.Last().Key;
            LogVerbose($"Time-based inventory association: Channel {inventoryChannelIndex} -> Last player {lastPlayer}");
            return lastPlayer;
        }

        return null;
    }


    private void AnalyzeUnknownExport(object? export, uint channelIndex, string context)
    {
        if (export == null) return;

        var type = export.GetType();
        var typeName = type.Name;

        // Track unique unknown export types
        unknownExportTypes.Add($"{typeName} ({context})");

        LogVerbose($"UNKNOWN EXPORT {context}: {typeName} on Channel {channelIndex}");

        // Special handling for FortClientObservedStat
        if (export is FortClientObservedStat clientStat)
        {
            var statName = clientStat.StatName;
            var statValue = clientStat.StatValue;

            if (!allObservedStats.ContainsKey(statName))
            {
                allObservedStats[statName] = new List<object>();
            }

            if (statValue.HasValue)
            {
                allObservedStats[statName].Add(statValue.Value);
            }
        }

        // Log all properties for debugging with enhanced tournament filtering
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        LogVerbose($"  ALL PROPERTIES:");
        foreach (var prop in properties)
        {
            try
            {
                var value = prop.GetValue(export);
                var propName = prop.Name.ToLower();

                LogVerbose($"    {prop.Name} = {value} ({prop.PropertyType.Name})");

                // Highlight tournament-relevant properties
                if (propName.Contains("material") || propName.Contains("gather") ||
                    propName.Contains("travel") || propName.Contains("distance") ||
                    propName.Contains("harvest") || propName.Contains("resource") ||
                    propName.Contains("farm") || propName.Contains("collect") ||
                    propName.Contains("total") || propName.Contains("assist") ||
                    propName.Contains("damage") || propName.Contains("elim") ||
                    propName.Contains("wood") || propName.Contains("stone") ||
                    propName.Contains("metal") || propName.Contains("build") ||
                    propName.Contains("place") || propName.Contains("construct"))
                {
                    LogVerbose($"    🎯 POTENTIAL TOURNAMENT PROPERTY: {typeName}.{prop.Name} = {value}");
                }
            }
            catch (Exception ex)
            {
                LogVerbose($"    {prop.Name} = <error: {ex.Message}>");
            }
        }
    }

    private string GetPlayerIdFromActor(uint? actorId)
    {
        if (!actorId.HasValue || actorId.Value == 0) return "Unknown";

        if (actorGuidToPlayerId.TryGetValue(actorId.Value, out var playerId))
        {
            return playerId;
        }
        return "Unknown";
    }

    protected override void OnExportRead(uint channelIndex, INetFieldExportGroup? exportGroup)
    {

        // Log EVERY SINGLE export type
        if (exportGroup != null)
        {
            allExportTypes.Add(exportGroup.GetType().FullName);
        }

        // Catch harvest RPC at the top level - FIRST!
        if (exportGroup is PlayerDamagedResourceBuilding harvestRpc)
        {
            Console.WriteLine($"\n💎💎💎 HARVEST RPC FOUND via OnExportRead!");
            Console.WriteLine($"  Channel: {channelIndex}");
            Console.WriteLine($"  ResourceCount: {harvestRpc.PotentialResourceCount}");
            Console.WriteLine($"  ResourceType: {harvestRpc.PotentialResourceType}");
            Console.WriteLine($"  Weakspot: {harvestRpc.bJustHitWeakspot}");

            string playerId = "Unknown";
            if (broadcastChannelOwner.TryGetValue(channelIndex, out var ownerGuid))
            {
                playerId = GetPlayerIdFromActor(ownerGuid);
            }
            Console.WriteLine($"  Player: {playerId}");

            var matchingHit = recentHarvestHits.FirstOrDefault(h => h.PlayerId == playerId);
            string materialType = matchingHit?.MaterialType ?? (harvestRpc.PotentialResourceType switch
            {
                0 => "Wood",
                1 => "Stone",
                2 => "Metal",
                _ => "Unknown"
            });

            HarvestTracker.RecordDirectHarvest(
                playerId,
                materialType,
                harvestRpc.PotentialResourceCount,
                harvestRpc.bJustHitWeakspot,
                (float) Builder.GetCurrentTimeDouble()
            );

            Console.WriteLine($"  ✅ Recorded: {playerId} got {harvestRpc.PotentialResourceCount} {materialType}");
        }

        if (exportGroup is PlayerPawnCache pawnCache)
        {
            if (pawnCache.AnyRpc != null)
            {
                LogVerbose($"[RPC Detected] Channel={channelIndex}, Type={exportGroup.GetType().Name}");
            }
        }
        if (exportGroup != null)
        {
            var typeName = exportGroup.GetType().Name;
            TrackExportOnChannel(channelIndex, typeName);

            // Deep scan for harvest-related content
            var harvestKeywords = new[] { "harvest", "wood", "stone", "metal", "resource", "material", "pickaxe", "tree", "rock", "destruct" };
            var isHarvestRelated = false;
            var harvestDetails = new List<string>();

            // Check type name
            if (harvestKeywords.Any(keyword => typeName.ToLower().Contains(keyword)))
            {
                isHarvestRelated = true;
                harvestDetails.Add($"Type name: {typeName}");
            }

            // Deep scan all properties and nested properties
            ScanObjectForHarvestContent(exportGroup, "", harvestKeywords, ref isHarvestRelated, harvestDetails);

            if (isHarvestRelated)
            {
                LogVerbose($"=== HARVEST CANDIDATE ===");
                LogVerbose($"Type: {typeName}");
                LogVerbose($"Channel: {channelIndex}");

                // Get the NetField path
                var attributes = exportGroup.GetType().GetCustomAttributes(typeof(NetFieldExportGroupAttribute), false);
                foreach (NetFieldExportGroupAttribute attr in attributes)
                {
                    LogVerbose($"Path: {attr.Path}");
                }

                LogVerbose($"Harvest Evidence:");
                foreach (var detail in harvestDetails)
                {
                    LogVerbose($"  {detail}");
                }

                LogVerbose($"All Properties:");
                PrintAllProperties(exportGroup, "  ");
                LogVerbose($"========================");
            }

            LogVerbose($"EXPORT: {typeName} on Channel {channelIndex}");

            if (typeName == "GameState")
            {
                channelTypes[channelIndex] = "GAMESTATE";
            }
            else if (typeName == "FortPlayerState")
            {
                channelTypes[channelIndex] = "PLAYER_STATE";
            }
            else if (typeName == "PlayerPawn")
            {
                channelTypes[channelIndex] = "PLAYER_PAWN";
            }
        }

        switch (exportGroup)
        {
            case FortBroadcastRemoteClientInfo remoteClientInfo:
                // Declare playerId ONCE at the top of this case
                string playerId = "Unknown";
                if (broadcastChannelOwner.TryGetValue(channelIndex, out var ownerGuid))
                {
                    playerId = GetPlayerIdFromActor(ownerGuid);
                }

                // Store the owner whenever we see it
                if (remoteClientInfo.Owner.HasValue)
                {
                    broadcastChannelOwner[channelIndex] = remoteClientInfo.Owner.Value;
                    playerId = GetPlayerIdFromActor(remoteClientInfo.Owner.Value);
                }

                // Check for harvest HIT (geometry data)
                bool hasHarvestHit = remoteClientInfo.ParentObject.HasValue &&
                                     remoteClientInfo.Position != null;

                if (hasHarvestHit)
                {
                    var objectId = remoteClientInfo.ParentObject.Value;
                    var currentTime = (float) Builder.GetCurrentTimeDouble();

                    Console.WriteLine($"\n🪓 HARVEST HIT!");
                    Console.WriteLine($"  Player: {playerId}");
                    Console.WriteLine($"  Object GUID: {objectId}");
                    Console.WriteLine($"  Hit #{remoteClientInfo.HitCount}");

                    string materialType = "Unknown";
                    string blueprintClass = "Unknown";
                    HarvestableData harvestData = null;

                    if (_netGuidCache.TryGetPathName(objectId, out var objectPath))
                    {
                        Console.WriteLine($"  🎯 Object: {objectPath}");

                        // Extract blueprint class
                        blueprintClass = ExtractBlueprintClass(objectPath);

                        // Try to get from database
                        if (harvestableDatabase.TryGetHarvestable(blueprintClass, out harvestData))
                        {
                            materialType = harvestData.resourceType;
                            Console.WriteLine($"  ✅ Known harvestable: {materialType} (Yield: {harvestData.materialYield})");
                        }
                        else
                        {
                            // Unknown - track it for later
                            materialType = GetMaterialTypeFromArchetype(objectPath);
                            Console.WriteLine($"  ❓ Unknown harvestable - guessing: {materialType}");

                            // Track for export
                            TrackUnknownHarvestable(objectPath, objectPath, playerId, currentTime);
                        }

                        Console.WriteLine($"  📦 Material: {materialType}");
                    }
                    else
                    {
                        Console.WriteLine($"  ⚠️ No object path found for GUID {objectId}");
                    }

                    // Store this hit for correlation with RPC
                    recentHarvestHits.Enqueue(new HarvestHit
                    {
                        PlayerId = playerId,
                        ObjectGuid = objectId,
                        MaterialType = materialType,
                        Timestamp = DateTime.UtcNow
                    });

                    // Keep queue manageable
                    while (recentHarvestHits.Count > 0 &&
                           (DateTime.UtcNow - recentHarvestHits.Peek().Timestamp).TotalSeconds > 5)
                    {
                        recentHarvestHits.Dequeue();
                    }
                }

                // Check AnyRpc - THIS IS WHERE THE HARVEST RPC COMES THROUGH
                if (remoteClientInfo.AnyRpc != null)
                {
                    if (remoteClientInfo.AnyRpc is PlayerDamagedResourceBuilding harvestFromAnyRpc)
                    {
                        Console.WriteLine($"\n💎 HARVEST RPC DETECTED (from AnyRpc)!");
                        Console.WriteLine($"  Player: {playerId}");
                        Console.WriteLine($"  BuildingSMActor: {harvestFromAnyRpc.BuildingSMActor}");
                        Console.WriteLine($"  ResourceType: {harvestFromAnyRpc.PotentialResourceType}");
                        Console.WriteLine($"  ResourceCount: {harvestFromAnyRpc.PotentialResourceCount}");
                        Console.WriteLine($"  Destroyed: {harvestFromAnyRpc.bDestroyed}");
                        Console.WriteLine($"  Hit Weakspot: {harvestFromAnyRpc.bJustHitWeakspot}");

                        // Try to match with recent hit
                        var matchingHit = recentHarvestHits.FirstOrDefault(h => h.PlayerId == playerId);

                        string materialType;
                        if (matchingHit != null)
                        {
                            materialType = matchingHit.MaterialType;
                            Console.WriteLine($"  ✅ Matched with hit on {materialType}");

                            // Remove matched hit
                            var tempList = recentHarvestHits.ToList();
                            tempList.Remove(matchingHit);
                            recentHarvestHits = new Queue<HarvestHit>(tempList);
                        }
                        else
                        {
                            // Fallback to RPC's resource type
                            materialType = harvestFromAnyRpc.PotentialResourceType switch
                            {
                                0 => "Wood",
                                1 => "Stone",
                                2 => "Metal",
                                _ => "Unknown"
                            };
                            Console.WriteLine($"  ⚠️ No matching hit, using RPC type: {materialType}");
                        }

                        // Record the harvest!
                        HarvestTracker.RecordDirectHarvest(
                            playerId,
                            materialType,
                            harvestFromAnyRpc.PotentialResourceCount,
                            harvestFromAnyRpc.bJustHitWeakspot,
                            (float) Builder.GetCurrentTimeDouble()
                        );

                        Console.WriteLine($"  ✓ Recorded: {playerId} harvested {harvestFromAnyRpc.PotentialResourceCount} {materialType}");
                    }
                    else if (remoteClientInfo.AnyRpc is AddMapMarker marker)
                    {
                        LogVerbose($"Map marker added by player {marker.PlayerID}");
                    }
                    else if (remoteClientInfo.AnyRpc is RemoveMapMarker removeMarker)
                    {
                        LogVerbose($"Map marker removed by player {removeMarker.PlayerID}");
                    }
                }

                // Also check the direct property (backup, in case it's populated there)
                if (remoteClientInfo.PlayerDamagedResourceBuilding != null)
                {
                    var harvestFromProperty = remoteClientInfo.PlayerDamagedResourceBuilding;
                    Console.WriteLine($"\n💎 HARVEST RPC (Direct property)!");

                    var matchingHit = recentHarvestHits.FirstOrDefault(h => h.PlayerId == playerId);

                    string materialType;
                    if (matchingHit != null)
                    {
                        materialType = matchingHit.MaterialType;
                        var tempList = recentHarvestHits.ToList();
                        tempList.Remove(matchingHit);
                        recentHarvestHits = new Queue<HarvestHit>(tempList);
                    }
                    else
                    {
                        materialType = harvestFromProperty.PotentialResourceType switch
                        {
                            0 => "Wood",
                            1 => "Stone",
                            2 => "Metal",
                            _ => "Unknown"
                        };
                    }

                    HarvestTracker.RecordDirectHarvest(
                        playerId,
                        materialType,
                        harvestFromProperty.PotentialResourceCount,
                        harvestFromProperty.bJustHitWeakspot,
                        (float) Builder.GetCurrentTimeDouble()
                    );

                    Console.WriteLine($"  ✓ Recorded: {playerId} harvested {harvestFromProperty.PotentialResourceCount} {materialType}");
                }

                break;

            case PlayerPawn pawn:
                LogVerbose($"\n=== PLAYER PAWN UPDATE ===");
                LogVerbose($"PawnUniqueID: {pawn.PawnUniqueID}");

                Builder.UpdatePlayerPawn(channelIndex, pawn);
                ProcessPlayerPawn(pawn, channelIndex);
                PrintObjectProperties(pawn, "PlayerPawn", 0);
                break;

            case BatchedDamageCues damageCues:
                ProcessDamage(damageCues, channelIndex);
                break;

            case FortInventory inventory:
                ProcessInventoryData(inventory, channelIndex, "EXPORT");
                Builder.UpdateInventory(channelIndex, inventory);
                break;

            case FortClientObservedStat clientStat:
                ProcessClientStat(clientStat, channelIndex);
                break;

            case SafeZoneIndicator safeZone:
                Builder.UpdateSafeZones(safeZone);
                break;

            case SupplyDropLlama llama:
                Builder.UpdateLlama(channelIndex, llama);
                break;

            case Models.NetFieldExports.SupplyDrop drop:
                Builder.UpdateSupplyDrop(channelIndex, drop);
                break;

            case FortPoiManager poimanager:
                Builder.UpdatePoiManager(poimanager);
                break;

            case BaseWeapon weapon:
                Builder.UpdateWeapon(channelIndex, weapon);
                break;

            case FortPickup pickup:
                if (IsMaterialItem(pickup.ItemDefinition?.Name))
                {
                    Console.WriteLine($"\n=== MATERIAL PICKUP DEBUG ===");
                    PrintObjectProperties(pickup, "FortPickup", 0);
                    Console.WriteLine($"=== END DEBUG ===\n");
                }
                break;

            case HealthSet healthSet:
                AnalyzeForStats(healthSet, "HealthSet", channelIndex);
                break;

            // Wood builds
            case WoodWall:
            case WoodArchwayWall:
            case WoodBraceWall:
            case WoodDoorSideWall:
            case WoodDoorWall:
            case WoodWindowSideWall:
                ProcessBuild(channelIndex, (BaseBuild) exportGroup, "Wall", "Wood");
                break;

            // Stone builds
            case StoneWall:
            case StoneArchwayWall:
            case StoneBraceWall:
            case StoneDoorSideWall:
            case StoneDoorWall:
            case StoneWindowSideWall:
                ProcessBuild(channelIndex, (BaseBuild) exportGroup, "Wall", "Stone");
                break;

            // Metal builds  
            case MetalWall:
            case MetalArchwayWall:
            case MetalBraceWall:
            case MetalDoorSideWall:
            case MetalDoorWall:
            case MetalWindowSideWall:
                ProcessBuild(channelIndex, (BaseBuild) exportGroup, "Wall", "Metal");
                break;

            // Wood floors
            case WoodFloor:
            case WoodBalconyIFloor:
            case WoodBalconySFloor:
                ProcessBuild(channelIndex, (BaseBuild) exportGroup, "Floor", "Wood");
                break;

            // Wood roofs
            case WoodRoof:
            case WoodRoofI:
                ProcessBuild(channelIndex, (BaseBuild) exportGroup, "Roof", "Wood");
                break;

            // Wood stairs
            case WoodStair:
            case WoodStairF:
            case WoodStairT:
                ProcessBuild(channelIndex, (BaseBuild) exportGroup, "Stair", "Wood");
                break;

            // Stone floors
            case StoneFloor:
                ProcessBuild(channelIndex, (BaseBuild) exportGroup, "Floor", "Stone");
                break;

            // Stone roofs
            case StoneRoof:
                ProcessBuild(channelIndex, (BaseBuild) exportGroup, "Roof", "Stone");
                break;

            // Stone stairs
            case StoneStair:
            case StoneStairF:
            case StoneStairT:
                ProcessBuild(channelIndex, (BaseBuild) exportGroup, "Stair", "Stone");
                break;

            // Metal floors
            case MetalFloor:
                ProcessBuild(channelIndex, (BaseBuild) exportGroup, "Floor", "Metal");
                break;

            // Metal roofs
            case MetalRoof:
                ProcessBuild(channelIndex, (BaseBuild) exportGroup, "Roof", "Metal");
                break;

            // Metal stairs
            case MetalStair:
            case MetalStairF:
                ProcessBuild(channelIndex, (BaseBuild) exportGroup, "Stair", "Metal");
                break;

            default:
                if (exportGroup != null)
                {
                    AnalyzeUnknownExport(exportGroup, channelIndex, "EXPORT");

                    // Log potentially missed harvest exports
                    var typeName = exportGroup.GetType().Name;
                    if (!typeName.Contains("Unknown") && !typeName.Contains("Generic"))
                    {
                        LogVerbose($"UNHANDLED EXPORT: {typeName} on channel {channelIndex}");
                    }
                }
                break;
        }
    }

    private object GetPropertyValue(object obj, string propertyName)
    {
        try
        {
            var prop = obj.GetType().GetProperty(propertyName);
            return prop?.GetValue(obj);
        }
        catch
        {
            return null;
        }
    }

    private void PrintObjectProperties(object obj, string name, int indentLevel, HashSet<object>? visited = null)
    {
        if (obj == null) return;

        visited ??= new HashSet<object>();

        // Prevent infinite recursion on circular references
        if (visited.Contains(obj))
        {
            LogVerbose($"{GetIndent(indentLevel)}{name}: [Circular Reference]");
            return;
        }

        var type = obj.GetType();

        // Handle primitives and strings
        if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal))
        {
            LogVerbose($"{GetIndent(indentLevel)}{name}: {obj}");
            return;
        }

        visited.Add(obj);

        LogVerbose($"{GetIndent(indentLevel)}{name} ({type.Name}):");

        // Get all properties
        var properties = type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        foreach (var prop in properties)
        {
            try
            {
                var value = prop.GetValue(obj);

                if (value == null)
                {
                    LogVerbose($"{GetIndent(indentLevel + 1)}{prop.Name}: null");
                }
                else if (prop.PropertyType.IsPrimitive || prop.PropertyType == typeof(string) || prop.PropertyType == typeof(decimal))
                {
                    LogVerbose($"{GetIndent(indentLevel + 1)}{prop.Name}: {value}");
                }
                else if (prop.PropertyType.IsArray)
                {
                    var array = value as Array;
                    if (array != null)
                    {
                        LogVerbose($"{GetIndent(indentLevel + 1)}{prop.Name}: Array[{array.Length}]");

                        if (array.Length > 0 && indentLevel < 2) // Limit depth
                        {
                            for (int i = 0; i < Math.Min(array.Length, 5); i++) // Show first 5 items
                            {
                                var item = array.GetValue(i);
                                PrintObjectProperties(item, $"[{i}]", indentLevel + 2, visited);
                            }
                            if (array.Length > 5)
                            {
                                LogVerbose($"{GetIndent(indentLevel + 2)}... and {array.Length - 5} more items");
                            }
                        }
                    }
                }
                else if (prop.PropertyType.IsEnum)
                {
                    LogVerbose($"{GetIndent(indentLevel + 1)}{prop.Name}: {value} ({(int) value})");
                }
                else if (indentLevel < 2) // Limit depth to prevent too much nesting
                {
                    PrintObjectProperties(value, prop.Name, indentLevel + 1, visited);
                }
                else
                {
                    LogVerbose($"{GetIndent(indentLevel + 1)}{prop.Name}: {value.GetType().Name} [depth limit]");
                }
            }
            catch (Exception ex)
            {
                LogVerbose($"{GetIndent(indentLevel + 1)}{prop.Name}: [Error: {ex.Message}]");
            }
        }
    }

    private string GetIndent(int level)
    {
        return new string(' ', level * 2);
    }

    private bool IsMaterialItem(string? itemDefinitionName)
    {
        if (string.IsNullOrEmpty(itemDefinitionName)) return false;
        return itemDefinitionName.Contains("WoodItemData") ||
               itemDefinitionName.Contains("StoneItemData") ||
               itemDefinitionName.Contains("MetalItemData");
    }

    private void ProcessBuild(uint channelIndex, BaseBuild build, string buildTypeName, string material)
    {
        if (!(build.bPlayerPlaced == true || build.bIsInitiallyBuilding == true))
            return;

        if (!build.TeamIndex.HasValue)
            return;

        var currentTimeValue = Builder.GetCurrentTimeDouble();
        var currentTime = (float) currentTimeValue;

        LogVerbose($"\n=== BUILD: {material} {buildTypeName} at {currentTime}s ===");

        // Check if OwnerPersistentID is available
        if (build.OwnerPersistentID?.Value == null)
        {
            LogVerbose($"⚠️ No OwnerPersistentID - skipping build");
            return;
        }

        short persistentId = build.OwnerPersistentID.Value.Value;
        LogVerbose($"  OwnerPersistentID: {persistentId}");

        // Map persistent ID to Epic ID using PawnUniqueID mapping
        var epicId = GetEpicIdFromPersistentId(persistentId);

        if (epicId == null)
        {
            LogVerbose($"⚠️ Could not map OwnerPersistentID {persistentId} to Epic ID - skipping build");
            LogVerbose($"   Available PawnUniqueID mappings: {string.Join(", ", persistentIdToPlayerId.Keys.OrderBy(x => x))}");
            LogVerbose($"   Available Player→Epic mappings: {string.Join(", ", playerIdToEpicId.Keys)}");
            return;
        }

        LogVerbose($"✓ Mapped to Epic ID: {epicId}");

        var actorGuid = channelToActorGuid.GetValueOrDefault(channelIndex);

        BuildTracker.RecordBuild(
            actorGuid,
            epicId.ToLower(),
            $"{material}_{buildTypeName}",
            build.bPlayerPlaced ?? true,
            build.bDestroyed ?? false,
            build.TeamIndex.Value,
            build.Health ?? 100,
            build.EditingPlayer?.Value,
            currentTime
        );

        LogVerbose($"✓ Build recorded for Epic ID: {epicId}");
    }

    private string GetEpicIdFromPersistentId(short persistentId)
    {
        LogVerbose($"\n🔍 Looking up PersistentID: {persistentId}");

        // Step 1: PawnUniqueID (persistentId) -> Internal PlayerId
        if (persistentIdToPlayerId.TryGetValue(persistentId, out var internalPlayerId))
        {
            LogVerbose($"  ✓ Step 1: Found internal player ID: {internalPlayerId}");

            // Step 2: Internal PlayerId -> Epic ID
            if (playerIdToEpicId.TryGetValue(internalPlayerId, out var epicId))
            {
                LogVerbose($"  ✓ Step 2: Found Epic ID: {epicId}");
                return epicId;
            }

            LogVerbose($"  ⚠️ Step 2 failed: Internal player {internalPlayerId} found but no Epic ID mapping");
            LogVerbose($"     Available Epic ID mappings: {string.Join(", ", playerIdToEpicId.Keys)}");
            return internalPlayerId; // Fallback to internal ID
        }

        LogVerbose($"  ✗ Step 1 failed: No mapping found for PersistentID {persistentId}");
        LogVerbose($"     Available PersistentID mappings: {string.Join(", ", persistentIdToPlayerId.Keys.OrderBy(x => x))}");
        return null;
    }



    private void ScanObjectForHarvestContent(object obj, string path, string[] keywords, ref bool isHarvestRelated, List<string> harvestDetails, int depth = 0)
    {
        if (obj == null || depth > 3) return; // Prevent infinite recursion

        var objType = obj.GetType();
        var properties = objType.GetProperties();

        foreach (var prop in properties)
        {
            try
            {
                var value = prop.GetValue(obj);
                if (value == null) continue;

                var propPath = string.IsNullOrEmpty(path) ? prop.Name : $"{path}.{prop.Name}";
                var valueStr = value.ToString();

                // Check property name for harvest keywords
                if (keywords.Any(keyword => prop.Name.ToLower().Contains(keyword)))
                {
                    isHarvestRelated = true;
                    harvestDetails.Add($"Property name: {propPath}");
                }

                // Check property value for harvest keywords
                if (keywords.Any(keyword => valueStr.ToLower().Contains(keyword)))
                {
                    isHarvestRelated = true;
                    harvestDetails.Add($"Property value: {propPath} = {valueStr}");
                }

                // Recursively scan nested objects
                if (!IsSimpleType(value.GetType()) && depth < 3)
                {
                    ScanObjectForHarvestContent(value, propPath, keywords, ref isHarvestRelated, harvestDetails, depth + 1);
                }

                // Special handling for collections
                if (value is System.Collections.IEnumerable enumerable && !(value is string))
                {
                    int index = 0;
                    foreach (var item in enumerable)
                    {
                        if (item != null && !IsSimpleType(item.GetType()) && index < 5) // Limit to first 5 items
                        {
                            ScanObjectForHarvestContent(item, $"{propPath}[{index}]", keywords, ref isHarvestRelated, harvestDetails, depth + 1);
                        }
                        index++;
                    }
                }
            }
            catch (Exception ex)
            {
                // Ignore reflection errors
                LogVerbose($"Error scanning {prop.Name}: {ex.Message}");
            }
        }
    }


    private void PrintAllProperties(object obj, string indent, int depth = 0)
    {
        if (obj == null || depth > 2) return;

        var objType = obj.GetType();
        var properties = objType.GetProperties();

        foreach (var prop in properties)
        {
            try
            {
                var value = prop.GetValue(obj);
                if (value == null)
                {
                    LogVerbose($"{indent}{prop.Name}: null");
                }
                else if (IsSimpleType(value.GetType()))
                {
                    LogVerbose($"{indent}{prop.Name}: {value}");
                }
                else if (value is System.Collections.IEnumerable enumerable && !(value is string))
                {
                    LogVerbose($"{indent}{prop.Name}: [Collection]");
                    int index = 0;
                    foreach (var item in enumerable)
                    {
                        if (index < 3) // Show first 3 items
                        {
                            LogVerbose($"{indent}  [{index}]: {item}");
                        }
                        index++;
                        if (index >= 3) break;
                    }
                }
                else
                {
                    LogVerbose($"{indent}{prop.Name}: {value.GetType().Name}");
                    if (depth < 2)
                    {
                        PrintAllProperties(value, indent + "  ", depth + 1);
                    }
                }
            }
            catch (Exception ex)
            {
                LogVerbose($"{indent}{prop.Name}: Error - {ex.Message}");
            }
        }
    }

    private bool IsSimpleType(Type type)
    {
        return type.IsPrimitive ||
               type == typeof(string) ||
               type == typeof(DateTime) ||
               type == typeof(decimal) ||
               type == typeof(Guid) ||
               type.IsEnum;
    }

    private void ProcessPlayerState(FortPlayerState state, uint channelIndex)
    {
        var channel = Channels[channelIndex];
        var actorGuid = channel?.Actor?.ActorNetGUID?.Value;
        var realEpicId = state.UniqueId ?? state.UniqueID;
        var platformId = state.PlatformUniqueNetId;
        var botId = state.BotUniqueId;
        var isBot = state.bIsABot == true;

        var actualPlayerId = isBot ? botId : (realEpicId ?? platformId);

        LogVerbose($"=== PLAYER STATE on Channel {channelIndex} ===");
        LogVerbose($"Player ID: {actualPlayerId}");
        LogVerbose($"Actor GUID: {actorGuid}");
        LogVerbose($"Is Bot: {isBot}");

        if (!string.IsNullOrEmpty(actualPlayerId))
        {
            string playerName = actualPlayerId;

            var playerStateType = state.GetType();
            var nameProperties = new[] { "PlayerName", "PlayerNamePrivate", "Name", "DisplayName", "UserName" };

            foreach (var propName in nameProperties)
            {
                var property = playerStateType.GetProperty(propName);
                if (property != null && property.PropertyType == typeof(string))
                {
                    var value = property.GetValue(state) as string;
                    if (!string.IsNullOrEmpty(value))
                    {
                        playerName = value;
                        break;
                    }
                }
            }

            playerNames[actualPlayerId] = playerName;
            LogVerbose($"Player Name: {playerName}");
        }

        if (actorGuid.HasValue && !string.IsNullOrEmpty(actualPlayerId))
        {
            actorGuidToPlayerId[actorGuid.Value] = actualPlayerId;
            if (!playerInfoByPlayerId.ContainsKey(actualPlayerId))
            {
                // ADD TEAMINDEX HERE - Create PlayerInfo with TeamIndex
                var playerInfo = new PlayerInfo(actualPlayerId, channelIndex, actorGuid.Value, actualPlayerId);

                // Set TeamIndex from state if available
                if (state.TeamIndex.HasValue)
                {
                    playerInfo.TeamIndex = (byte) state.TeamIndex.Value;
                    LogVerbose($"Team Index: {state.TeamIndex.Value}");
                }

                playerInfoByPlayerId[actualPlayerId] = playerInfo;
                LogVerbose($"MAPPED: Actor {actorGuid.Value} -> Player {actualPlayerId}");
                DamageTracker.MapActorToPlayer(actorGuid.Value, actualPlayerId);
            }
            else
            {
                // Update existing PlayerInfo with TeamIndex if needed
                if (state.TeamIndex.HasValue)
                {
                    playerInfoByPlayerId[actualPlayerId].TeamIndex = (byte) state.TeamIndex.Value;
                }
            }
        }



        var stateType = state.GetType();
        var knownMaterialProps = new[] {
        "CurrentWood", "CurrentStone", "CurrentMetal",
        "Wood", "Stone", "Metal",
        "ResourceWood", "ResourceStone", "ResourceMetal",
        "WoodCount", "StoneCount", "MetalCount"
        };


        uint? wood = null, stone = null, metal = null;

        foreach (var propName in knownMaterialProps)
        {
            var property = stateType.GetProperty(propName);
            if (property != null)
            {
                try
                {
                    var value = property.GetValue(state);
                    LogVerbose($"  FOUND MATERIAL PROPERTY: {propName} = {value} ({property.PropertyType.Name})");

                    if (value is int intVal && intVal >= 0 && intVal <= 500)
                    {
                        if (propName.ToLower().Contains("wood")) wood = (uint) intVal;
                        else if (propName.ToLower().Contains("stone")) stone = (uint) intVal;
                        else if (propName.ToLower().Contains("metal")) metal = (uint) intVal;
                    }
                    else if (value is uint uintVal && uintVal >= 0 && uintVal <= 500)
                    {
                        if (propName.ToLower().Contains("wood")) wood = uintVal;
                        else if (propName.ToLower().Contains("stone")) stone = uintVal;
                        else if (propName.ToLower().Contains("metal")) metal = uintVal;
                    }

                    if (!string.IsNullOrEmpty(actualPlayerId))
                    {
                        if (!playerRealtimeStats.ContainsKey(actualPlayerId))
                            playerRealtimeStats[actualPlayerId] = new Dictionary<string, object>();

                        playerRealtimeStats[actualPlayerId][propName] = value ?? 0;
                    }
                }
                catch (Exception ex)
                {
                    LogVerbose($"  {propName}: <error reading: {ex.Message}>");
                }
            }
        }


        AnalyzeForStats(state, "FortPlayerState", channelIndex, actualPlayerId);
    }

    private void ProcessPlayerPawn(PlayerPawn pawn, uint channelIndex)
    {
        var pawnChannel = Channels[channelIndex];
        var pawnActorGuid = pawnChannel?.Actor?.ActorNetGUID?.Value;
        var playerStateActorGuid = pawn.PlayerState;

        LogVerbose($"=== PLAYER PAWN on Channel {channelIndex} ===");
        LogVerbose($"Pawn Actor GUID: {pawnActorGuid}");
        LogVerbose($"PlayerState GUID: {playerStateActorGuid}");

        string? pawnPlayerId = null;
        if (pawnActorGuid.HasValue && actorGuidToPlayerId.ContainsKey(pawnActorGuid.Value))
        {
            pawnPlayerId = actorGuidToPlayerId[pawnActorGuid.Value];
            LogVerbose($"✓ Found player via pawn actor GUID: {pawnPlayerId}");
        }
        else if (playerStateActorGuid.HasValue && actorGuidToPlayerId.ContainsKey(playerStateActorGuid.Value))
        {
            pawnPlayerId = actorGuidToPlayerId[playerStateActorGuid.Value];
            LogVerbose($"✓ Found player via PlayerState GUID: {pawnPlayerId}");
            if (pawnActorGuid.HasValue)
            {
                actorGuidToPlayerId[pawnActorGuid.Value] = pawnPlayerId;
                DamageTracker.MapActorToPlayer(pawnActorGuid.Value, pawnPlayerId);
                LogVerbose($"✓ LINKED PAWN: Actor {pawnActorGuid.Value} -> Player {pawnPlayerId}");
            }
        }
        else
        {
            LogVerbose($"⚠️ Could not identify player for pawn");
        }

        LogVerbose($"Final Player: {pawnPlayerId ?? "UNKNOWN"}");

        // === NEW: Check for PawnUniqueID and map it ===
        var pawnType = pawn.GetType();
        var pawnIdProp = pawnType.GetProperty("PawnUniqueID");

        if (pawnIdProp != null)
        {
            try
            {
                var pawnUniqueId = pawnIdProp.GetValue(pawn);
                LogVerbose($"PawnUniqueID: {pawnUniqueId} (Type: {pawnIdProp.PropertyType.Name})");

                // Try to convert it to short and map it
                if (pawnUniqueId != null && !string.IsNullOrEmpty(pawnPlayerId))
                {
                    short persistentId = 0;
                    bool canMap = false;

                    if (pawnUniqueId is short shortVal)
                    {
                        persistentId = shortVal;
                        canMap = true;
                    }
                    else if (pawnUniqueId is int intVal && intVal >= short.MinValue && intVal <= short.MaxValue)
                    {
                        persistentId = (short) intVal;
                        canMap = true;
                    }
                    else if (pawnUniqueId is byte byteVal)
                    {
                        persistentId = (short) byteVal;
                        canMap = true;
                    }
                    else if (pawnUniqueId is uint uintVal && uintVal <= (uint) short.MaxValue)
                    {
                        persistentId = (short) uintVal;
                        canMap = true;
                    }

                    if (canMap && persistentId != 0)
                    {
                        if (!persistentIdToPlayerId.ContainsKey(persistentId))
                        {
                            persistentIdToPlayerId[persistentId] = pawnPlayerId;
                            LogVerbose($"✓ Mapped PawnUniqueID {persistentId} -> Player {pawnPlayerId}");

                            // Also try to get Epic ID for complete mapping
                            if (playerIdToEpicId.TryGetValue(pawnPlayerId, out var epicId))
                            {
                                LogVerbose($"  └─ Epic ID: {epicId}");
                            }
                        }
                        else
                        {
                            LogVerbose($"⚠ PawnUniqueID {persistentId} already mapped");
                        }
                    }
                    else if (persistentId == 0)
                    {
                        LogVerbose($"⚠ PawnUniqueID is 0 (invalid) - skipping mapping");
                    }
                    else
                    {
                        LogVerbose($"⚠ PawnUniqueID value cannot be converted to short");
                    }
                }
            }
            catch (Exception ex)
            {
                LogVerbose($"✗ Error reading PawnUniqueID: {ex.Message}");
            }
        }
        else
        {
            LogVerbose($"⚠ PawnUniqueID property not found");
        }
        // === END PawnUniqueID mapping ===

        // Track player position for build ownership
        if (!string.IsNullOrEmpty(pawnPlayerId))
        {
            Vector3 position = default;
            bool hasPosition = false;

            // Try to get position from Location first
            if (pawn.Location != null)
            {
                position = new Vector3((float) pawn.Location.X, (float) pawn.Location.Y, (float) pawn.Location.Z);
                hasPosition = true;
                LogVerbose($"✓ Got position from pawn.Location: {position}");
            }
            // Fallback to ReplicatedMovement
            else if (pawn.ReplicatedMovement.HasValue && pawn.ReplicatedMovement.Value.Location != null)
            {
                var loc = pawn.ReplicatedMovement.Value.Location;
                position = new Vector3((float) loc.X, (float) loc.Y, (float) loc.Z);
                hasPosition = true;
                LogVerbose($"✓ Got position from ReplicatedMovement: {position}");
            }
            else
            {
                LogVerbose($"⚠️ No position data available (Location: {pawn.Location != null}, ReplicatedMovement: {pawn.ReplicatedMovement.HasValue})");
            }

            if (hasPosition)
            {
                LogVerbose($"📍 TRACKING POSITION for {pawnPlayerId}: {position}");

                var teamIndex = GetPlayerTeamIndex(pawnPlayerId);
                LogVerbose($"   Team Index: {teamIndex}");

                _buildOwnershipTracker.OnPlayerUpdate(
                    pawnActorGuid ?? 0,
                    GetPlayerNameFromReplay(pawnPlayerId) ?? "Unknown",
                    teamIndex,
                    position,
                    GetCurrentGameTime()
                );

                var fvector = new FVector(position.X, position.Y, position.Z);
                _buildAttributionTracker.UpdatePlayerLocation(
                    pawnPlayerId,
                    fvector,
                    GetCurrentGameTime()
                );

                LogVerbose($"✓ Position tracked for {pawnPlayerId}");
            }
            else
            {
                LogVerbose($"❌ No valid position found for pawn of player {pawnPlayerId}");
            }
        }

        AnalyzeForStats(pawn, "PlayerPawn", channelIndex, pawnPlayerId);
    }

    private FVector? GetBuildLocation(uint channelIndex)
    {
        var channel = Channels[channelIndex];
        if (channel?.Actor?.Location != null)
        {
            return channel.Actor.Location;
        }
        return null;
    }

    private FVector? GetPlayerLocation(string playerId)
    {
        // Find player's pawn channel
        var playerInfo = playerInfoByPlayerId.GetValueOrDefault(playerId);
        if (playerInfo != null)
        {
            var channel = Channels[playerInfo.ChannelIndex];
            if (channel?.Actor?.Location != null)
            {
                return channel.Actor.Location;  // Returns FVector (not nullable)
            }
        }
        return null;  // Can return null
    }

    private int GetPlayerTeamIndex(string playerId)
    {
        if (playerInfoByPlayerId.ContainsKey(playerId))
        {
            return playerInfoByPlayerId[playerId].TeamIndex;
        }
        return -1;
    }

    private void ProcessClientStat(FortClientObservedStat clientStat, uint channelIndex)
    {
        LogVerbose($"=== CLIENT STAT on Channel {channelIndex} ===");
        LogVerbose($"Stat: {clientStat.StatName} = {clientStat.StatValue}");

        string? playerId = null;

        // Method 1: Try builder's channel lookup first
        playerId = Builder.GetPlayerIdFromChannel(channelIndex);

        // Method 2: Cross-channel association
        if (string.IsNullOrEmpty(playerId))
        {
            playerId = TryAssociateStatWithPlayer(channelIndex);
        }

        // Method 3: Direct actor GUID lookup
        if (string.IsNullOrEmpty(playerId))
        {
            var statChannel = Channels[channelIndex];
            if (statChannel?.Actor?.ActorNetGUID != null)
            {
                var statActorGuid = statChannel.Actor.ActorNetGUID.Value;
                actorGuidToPlayerId.TryGetValue(statActorGuid, out playerId);
            }
        }

        if (!string.IsNullOrEmpty(playerId))
        {
            LogVerbose($"Associated with player: {playerId}");

            if (!playerRealtimeStats.ContainsKey(playerId))
            {
                playerRealtimeStats[playerId] = new Dictionary<string, object>();
            }

            playerRealtimeStats[playerId][clientStat.StatName] = clientStat.StatValue;
            LogVerbose($"*** STAT LOGGED: {clientStat.StatName} = {clientStat.StatValue} for {playerId} ***");
        }
        else
        {
            LogVerbose($"Could not associate with any player - storing as unassociated");

            if (!channelStats.ContainsKey(channelIndex))
            {
                channelStats[channelIndex] = new Dictionary<string, object>();
            }
            channelStats[channelIndex][clientStat.StatName] = clientStat.StatValue;
        }
    }

    private string? TryAssociateStatWithPlayer(uint statChannelIndex)
    {
        // Pattern 1: Stats on odd channels, players on even channels
        if (statChannelIndex % 2 == 1)
        {
            var playerChannelIndex = statChannelIndex + 1;
            var playerId = Builder.GetPlayerIdFromChannel(playerChannelIndex);
            if (!string.IsNullOrEmpty(playerId))
            {
                LogVerbose($"Cross-channel association: Stat channel {statChannelIndex} -> Player channel {playerChannelIndex} -> {playerId}");
                return playerId;
            }
        }

        // Pattern 2: Try previous even channel if next doesn't work
        if (statChannelIndex % 2 == 1 && statChannelIndex > 1)
        {
            var playerChannelIndex = statChannelIndex - 1;
            var playerId = Builder.GetPlayerIdFromChannel(playerChannelIndex);
            if (!string.IsNullOrEmpty(playerId))
            {
                LogVerbose($"Cross-channel association (reverse): Stat channel {statChannelIndex} -> Player channel {playerChannelIndex} -> {playerId}");
                return playerId;
            }
        }

        return null;
    }

    private uint? FindPlayerChannelForActor(uint actorGuid)
    {
        foreach (var channel in exportTypeToChannels.GetValueOrDefault("FortPlayerState", new List<uint>()))
        {
            var channelObj = Channels[channel];
            if (channelObj?.Actor?.ActorNetGUID?.Value == actorGuid)
            {
                return channel;
            }
        }
        return null;
    }

    private void ProcessDamage(BatchedDamageCues damageCues, uint channelIndex)
    {
        var attackerChannel = Channels[channelIndex];
        string? attackerPlayerId = null;
        if (attackerChannel?.Actor?.ActorNetGUID != null)
        {
            var attackerActorGuid = attackerChannel.Actor.ActorNetGUID.Value;
            actorGuidToPlayerId.TryGetValue(attackerActorGuid, out attackerPlayerId);
        }

        string? victimPlayerId = null;
        if (damageCues.HitActor.HasValue)
        {
            actorGuidToPlayerId.TryGetValue(damageCues.HitActor.Value, out victimPlayerId);
        }

        if (!string.IsNullOrEmpty(attackerPlayerId) && !string.IsNullOrEmpty(victimPlayerId))
        {
            uint? damageAmount = damageCues.Magnitude.HasValue ? (uint) damageCues.Magnitude.Value : null;
            DamageTracker.RecordDamage(attackerPlayerId, victimPlayerId, damageAmount, false);
        }
    }

    private void AnalyzeForStats(object obj, string typeName, uint channelIndex, string? playerId = null)
    {
        var properties = obj.GetType().GetProperties();
        var allProps = new List<string>();

        foreach (var prop in properties)
        {
            try
            {
                var value = prop.GetValue(obj);
                allProps.Add($"{prop.Name}={value}");
                var propName = prop.Name.ToLower();

                if (!statPropertiesByType.ContainsKey(typeName))
                {
                    statPropertiesByType[typeName] = new HashSet<string>();
                }
                statPropertiesByType[typeName].Add(prop.Name);

                // Highlight tournament-relevant properties
                if (propName.Contains("material") || propName.Contains("gather") ||
                    propName.Contains("travel") || propName.Contains("distance") ||
                    propName.Contains("harvest") || propName.Contains("resource") ||
                    propName.Contains("farm") || propName.Contains("collect") ||
                    propName.Contains("total") || propName.Contains("assist") ||
                    propName.Contains("damage") || propName.Contains("elim") ||
                    propName.Contains("wood") || propName.Contains("stone") ||
                    propName.Contains("metal") || propName.Contains("build") ||
                    propName.Contains("place") || propName.Contains("construct"))
                {
                    LogVerbose($"🎯 POTENTIAL TOURNAMENT PROPERTY: {typeName}.{prop.Name} = {value} (Channel: {channelIndex}, Player: {playerId ?? "UNKNOWN"})");

                    if (!string.IsNullOrEmpty(playerId))
                    {
                        if (!playerRealtimeStats.ContainsKey(playerId))
                        {
                            playerRealtimeStats[playerId] = new Dictionary<string, object>();
                        }
                        playerRealtimeStats[playerId][$"{typeName}_{prop.Name}"] = value ?? "null";
                    }
                }
            }
            catch (Exception ex)
            {
                allProps.Add($"{prop.Name}=<error: {ex.Message}>");
            }
        }
    }

    private void PrintUniqueStatsSummary()
    {
        LogVerbose("\n=== UNIQUE STAT PROPERTIES FOUND ===");
        foreach (var typeGroup in statPropertiesByType.OrderBy(x => x.Key))
        {
            LogVerbose($"\n{typeGroup.Key}:");
            foreach (var prop in typeGroup.Value.OrderBy(x => x))
            {
                LogVerbose($"  - {prop}");
            }
        }
    }

    private void PrintStructuralAnalysis()
    {
        LogVerbose("\n=== COMPREHENSIVE STRUCTURAL ANALYSIS ===");

        LogVerbose("\n1. CHANNEL TYPES AND DISTRIBUTION:");
        foreach (var channelType in channelTypes.GroupBy(x => x.Value))
        {
            var channels = channelType.Select(x => x.Key).ToList();
            LogVerbose($"  {channelType.Key}: Channels {string.Join(", ", channels)}");
        }

        LogVerbose("\n2. EXPORT TYPE DISTRIBUTION:");
        foreach (var exportType in exportTypeToChannels.OrderBy(x => x.Key))
        {
            var uniqueChannels = exportType.Value.Distinct().ToList();
            LogVerbose($"  {exportType.Key}: Appears on {uniqueChannels.Count} channels [{string.Join(", ", uniqueChannels)}]");
        }

        LogVerbose("\n3. PLAYER-TO-ACTOR MAPPINGS:");
        foreach (var mapping in actorGuidToPlayerId)
        {
            LogVerbose($"  Actor {mapping.Key} -> Player {mapping.Value}");
        }

        LogVerbose("\n4. PLAYER REALTIME STATS:");
        foreach (var player in playerRealtimeStats)
        {
            LogVerbose($"  Player {player.Key}:");

            var currentMaterials = new List<string>();
            var farmingBuilding = new List<string>();
            var otherStats = new List<string>();

            foreach (var stat in player.Value)
            {
                if (stat.Key == "Wood" || stat.Key == "Stone" || stat.Key == "Metal")
                {
                    currentMaterials.Add($"{stat.Key}: {stat.Value}");
                }
                else if (stat.Key == "MaterialsFarmed" || stat.Key == "BuildsPlaced" ||
                        stat.Key.Contains("Farmed") || stat.Key.Contains("Builds"))
                {
                    farmingBuilding.Add($"{stat.Key}: {stat.Value}");
                }
                else
                {
                    otherStats.Add($"{stat.Key}: {stat.Value}");
                }
            }

            if (currentMaterials.Any())
            {
                LogVerbose($"    Current: {string.Join(", ", currentMaterials)}");
            }
            if (farmingBuilding.Any())
            {
                LogVerbose($"    Activity: {string.Join(", ", farmingBuilding)}");
            }
            if (otherStats.Any())
            {
                foreach (var stat in otherStats)
                {
                    LogVerbose($"    {stat}");
                }
            }
        }

        LogVerbose("\n5. UNASSOCIATED CHANNEL STATS:");
        foreach (var channel in channelStats)
        {
            LogVerbose($"  Channel {channel.Key}:");
            foreach (var stat in channel.Value)
            {
                LogVerbose($"    {stat.Key}: {stat.Value}");
            }
        }

        AnalyzeStatPatterns();
        PrintAllAvailableStats();
        PrintUniqueStatsSummary();
        PrintUnknownExportsSummary();
        _buildOwnershipTracker.PrintStatistics();


        // Print all unique export types at the end
        Console.WriteLine("\n=== ALL EXPORT TYPES SEEN ===");
        foreach (var type in allExportTypes.OrderBy(t => t))
        {
            Console.WriteLine($"  {type}");
        }
        Console.WriteLine($"Total unique types: {allExportTypes.Count}");
        Console.WriteLine("=============================\n");
    }


    private void AnalyzeStatPatterns()
    {
        LogVerbose("\n7. STAT PATTERN ANALYSIS:");

        var statChannels = exportTypeToChannels.ContainsKey("FortClientObservedStat")
            ? exportTypeToChannels["FortClientObservedStat"].Distinct().ToList()
            : new List<uint>();

        LogVerbose($"FortClientObservedStat appears on {statChannels.Count} channels: {string.Join(", ", statChannels)}");

        var inventoryChannels = exportTypeToChannels.ContainsKey("FortInventory")
            ? exportTypeToChannels["FortInventory"].Distinct().ToList()
            : new List<uint>();

        LogVerbose($"FortInventory appears on {inventoryChannels.Count} channels: {string.Join(", ", inventoryChannels)}");
    }

    private void PrintAllAvailableStats()
    {
        LogVerbose("\n=== ALL AVAILABLE STATS SUMMARY ===");

        var allStatNames = new HashSet<string>();
        foreach (var player in playerRealtimeStats.Values)
        {
            foreach (var statName in player.Keys)
            {
                allStatNames.Add(statName);
            }
        }

        LogVerbose($"\nFound {allStatNames.Count} unique stat types:");
        foreach (var statName in allStatNames.OrderBy(x => x))
        {
            LogVerbose($"  - {statName}");
        }
    }

    public override void ReadReplayHeader(FArchive archive)
    {
        base.ReadReplayHeader(archive);
        Branch = Replay.Header.Branch;
    }

    public override void ReadEvent(FArchive archive)
    {
        var info = new Unreal.Core.Models.EventInfo
        {
            Id = archive.ReadFString(),
            Group = archive.ReadFString(),
            Metadata = archive.ReadFString(),
            StartTime = archive.ReadUInt32(),
            EndTime = archive.ReadUInt32(),
            SizeInBytes = archive.ReadInt32()
        };

        var eventKey = $"{info.Group}/{info.Metadata}";
        if (!uniqueEvents.ContainsKey(eventKey))
        {
            uniqueEvents[eventKey] = new List<(string, string, int, uint)>();
        }
        uniqueEvents[eventKey].Add((info.Group, info.Metadata, info.SizeInBytes, info.StartTime));

        using var decryptedArchive = DecryptBuffer(archive, info.SizeInBytes);

        if (info.Group == ReplayEventTypes.PLAYER_ELIMINATION)
        {
            var elimination = ParseElimination(decryptedArchive, info);
            Replay.Eliminations.Add(elimination);

            var attackerId = elimination.EliminatorInfo?.Id;
            var victimId = elimination.EliminatedInfo?.Id;

            if (!string.IsNullOrEmpty(attackerId))
            {
                var attackerStats = DamageTracker.GetOrCreatePlayerStats(attackerId);
                attackerStats.Eliminations++;
            }

            if (!string.IsNullOrEmpty(victimId))
            {
                var victimStats = DamageTracker.GetOrCreatePlayerStats(victimId);
                victimStats.Deaths++;
            }
            return;
        }
        else if (info.Metadata == ReplayEventTypes.MATCH_STATS)
        {
            var playerStats = ParseMatchStats(decryptedArchive, info);
            AllPlayerStats.Add(playerStats);
            LogVerbose($"*** MATCH STATS EVENT #{AllPlayerStats.Count} ***");
            LogVerbose($"MaterialsGathered: {playerStats.MaterialsGathered}");
            LogVerbose($"MaterialsUsed: {playerStats.MaterialsUsed}");
            LogVerbose($"TotalTraveled: {playerStats.TotalTraveled}");
            LogVerbose($"Assists: {playerStats.Assists}");

            Replay.Stats = playerStats;
            return;
        }
        else if (info.Metadata == ReplayEventTypes.TEAM_STATS)
        {
            Replay.TeamStats = ParseTeamStats(decryptedArchive, info);
            return;
        }
        else if (info.Metadata == ReplayEventTypes.ENCRYPTION_KEY)
        {
            ParseEncryptionKeyEvent(decryptedArchive, info);
            return;
        }

        if (IsDebugMode)
        {
            throw new UnknownEventException(
                $"Unknown event {info.Group} ({info.Metadata}) of size {info.SizeInBytes}"
            );
        }
    }

    public void PrintEventSummary()
    {
        LogVerbose("\n=== UNIQUE EVENTS FOUND IN TOURNAMENT REPLAY ===");
        LogVerbose($"Total unique event types: {uniqueEvents.Count}");

        foreach (var eventType in uniqueEvents.OrderBy(x => x.Key))
        {
            var events = eventType.Value;
            var avgSize = events.Average(x => x.Size);
            var timeRange = events.Count > 1 ? $"{events.Min(x => x.Time)}-{events.Max(x => x.Time)}" : events.First().Time.ToString();

            LogVerbose($"\n{eventType.Key}:");
            LogVerbose($"  Count: {events.Count}");
            LogVerbose($"  Avg Size: {avgSize:F1} bytes");
            LogVerbose($"  Time Range: {timeRange}");

            var key = eventType.Key.ToLower();
            if (key.Contains("player") || key.Contains("match") || key.Contains("server") ||
                key.Contains("tournament") || key.Contains("competition") || key.Contains("stat"))
            {
                LogVerbose($"  🎯 POTENTIALLY RELEVANT FOR TOURNAMENT STATS");
            }
        }
    }

    public virtual EncryptionKey ParseEncryptionKeyEvent(FArchive archive, Unreal.Core.Models.EventInfo info) => new()
    {
        Info = info,
        Key = archive.ReadBytesToString(32)
    };

    public virtual TeamStats ParseTeamStats(FArchive archive, Unreal.Core.Models.EventInfo info) => new()
    {
        Info = info,
        Unknown = archive.ReadUInt32(),
        Position = archive.ReadUInt32(),
        TotalPlayers = archive.ReadUInt32()
    };

    public virtual Stats ParseMatchStats(FArchive archive, Unreal.Core.Models.EventInfo info) => new()
    {
        Info = info,
        Unknown = archive.ReadUInt32(),
        Accuracy = archive.ReadSingle(),
        Assists = archive.ReadUInt32(),
        Eliminations = archive.ReadUInt32(),
        WeaponDamage = archive.ReadUInt32(),
        OtherDamage = archive.ReadUInt32(),
        Revives = archive.ReadUInt32(),
        DamageTaken = archive.ReadUInt32(),
        DamageToStructures = archive.ReadUInt32(),
        MaterialsGathered = archive.ReadUInt32(),
        MaterialsUsed = archive.ReadUInt32(),
        TotalTraveled = archive.ReadUInt32()
    };

    public virtual PlayerElimination ParseElimination(FArchive archive, Unreal.Core.Models.EventInfo info)
    {
        try
        {
            var elim = new PlayerElimination { Info = info };
            var version = archive.ReadInt32();

            if (version >= 3)
            {
                archive.SkipBytes(1);

                if (version >= 6)
                {
                    elim.EliminatedInfo.Rotation = archive.ReadFQuat();
                    elim.EliminatedInfo.Location = archive.ReadFVector();
                    elim.EliminatedInfo.Scale = archive.ReadFVector();
                }

                elim.EliminatorInfo.Rotation = archive.ReadFQuat();
                elim.EliminatorInfo.Location = archive.ReadFVector();
                elim.EliminatorInfo.Scale = archive.ReadFVector();
            }
            else
            {
                if (Major <= 4 && Minor < 2)
                {
                    archive.SkipBytes(8);
                }
                else if (Major == 4 && Minor <= 2)
                {
                    archive.SkipBytes(36);
                }
            }

            if ((int) archive.EngineNetworkVersion >= 34)
            {
                archive.SkipBytes(80);
            }

            ParsePlayer(archive, elim.EliminatedInfo, version);
            ParsePlayer(archive, elim.EliminatorInfo, version);

            elim.GunType = archive.ReadByte();
            elim.Knocked = archive.ReadUInt32AsBoolean();
            elim.Time = info.StartTime.MillisecondsToTimeStamp();
            return elim;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error while parsing PlayerElimination at timestamp {}", info?.StartTime);
            throw new PlayerEliminationException($"Error while parsing PlayerElimination at timestamp {info?.StartTime}", ex);
        }
    }

    public virtual void ParsePlayer(FArchive archive, PlayerEliminationInfo info, int version)
    {
        if (version < 6)
        {
            info.Id = archive.ReadFString();
            return;
        }

        info.PlayerType = archive.ReadByteAsEnum<PlayerTypes>();
        info.Id = info.PlayerType switch
        {
            PlayerTypes.BOT => "Bot",
            PlayerTypes.NAMED_BOT => archive.ReadFString(),
            PlayerTypes.PLAYER => archive.ReadGUID(archive.ReadByte()),
            _ => ""
        };
    }

    protected override FArchive DecryptBuffer(FArchive archive, int size)
    {
        if (!Replay.Info.IsEncrypted)
        {
            return new Unreal.Core.BinaryReader(archive.ReadBytes(size))
            {
                EngineNetworkVersion = Replay.Header.EngineNetworkVersion,
                NetworkVersion = Replay.Header.NetworkVersion,
                ReplayHeaderFlags = Replay.Header.Flags,
                ReplayVersion = Replay.Info.FileVersion
            };
        }

        var key = Replay.Info.EncryptionKey;
        var encryptedBytes = archive.ReadBytes(size);

        using var aes = Aes.Create();
        aes.KeySize = key.Length * 8;
        aes.Key = key.ToArray();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.PKCS7;

        using var cryptoTransform = aes.CreateDecryptor();
        var decryptedArray = cryptoTransform.TransformFinalBlock(encryptedBytes.ToArray(), 0, encryptedBytes.Length);

        return new Unreal.Core.BinaryReader(decryptedArray.AsMemory())
        {
            EngineNetworkVersion = archive.EngineNetworkVersion,
            NetworkVersion = archive.NetworkVersion,
            ReplayHeaderFlags = archive.ReplayHeaderFlags,
            ReplayVersion = archive.ReplayVersion
        };
    }

    protected override FArchive Decompress(FArchive archive)
    {
        if (!Replay.Info.IsCompressed)
        {
            return archive;
        }

        var decompressedSize = archive.ReadInt32();
        var compressedSize = archive.ReadInt32();
        var compressedBuffer = archive.ReadBytes(compressedSize);

        _logger?.LogDebug("Decompressed archive from {compressedSize} to {decompressedSize}.", compressedSize, decompressedSize);
        var output = Oodle.DecompressReplayData(compressedBuffer, decompressedSize);

        return new Unreal.Core.BinaryReader(output)
        {
            EngineNetworkVersion = archive.EngineNetworkVersion,
            NetworkVersion = archive.NetworkVersion,
            ReplayHeaderFlags = archive.ReplayHeaderFlags,
            ReplayVersion = archive.ReplayVersion
        };
    }

    // Utility methods for accessing tracked data
    public int GetRealPlayerCount()
    {
        return playerInfoByPlayerId.Count;
    }

    public DamageTracker GetDamageTracker()
    {
        return DamageTracker;
    }

    public Dictionary<string, Dictionary<string, object>> GetPlayerRealtimeStats()
    {
        return playerRealtimeStats;
    }

    public Dictionary<uint, Dictionary<string, object>> GetChannelStats()
    {
        return channelStats;
    }

    public List<Stats> GetAllPlayerStats()
    {
        return AllPlayerStats;
    }

    public async Task ExportPlayerStatsToCSVAsync(string filePath)
    {
        var csvLines = new List<string>();
        var replayId = Path.GetFileNameWithoutExtension(filePath).Replace("player_stats_", "");

        var headers = new List<string>
    {
        "ReplayID", "EpicID", "PlayerName",
        "TotalBuildsPlaced", "WoodBuildsPlaced", "StoneBuildsPlaced", "MetalBuildsPlaced",
        "WallsPlaced", "FloorsPlaced", "StairsPlaced", "RoofsPlaced", "BuildsEdited", "BuildsDestroyed",
        "Eliminations", "DamageDealt", "DamageTaken", "ShotsHit",
        "TotalMaterialsHarvested", "WoodHarvested", "StoneHarvested", "MetalHarvested", "HarvestActions"
    };

        csvLines.Add(string.Join(",", headers));

        // Use case-insensitive HashSet to avoid duplicates
        var allPlayerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        allPlayerIds.UnionWith(playerRealtimeStats.Keys);
        allPlayerIds.UnionWith(DamageTracker.PlayerStats.Keys);
        allPlayerIds.UnionWith(HarvestTracker.GetAllPlayerStats().Keys);
        allPlayerIds.UnionWith(BuildTracker.GetAllPlayerStats().Keys);

        Console.WriteLine($"Processing {allPlayerIds.Count} unique players...");

        foreach (var playerId in allPlayerIds)
        {
            var row = new List<string> { replayId, playerId.ToLower() };

            // Get player name from replay data
            var playerName = GetPlayerNameFromReplay(playerId);

            // Only add player name if it's different from the player ID
            if (!string.IsNullOrEmpty(playerName) && !playerName.Equals(playerId, StringComparison.OrdinalIgnoreCase))
            {
                row.Add($"\"{playerName}\"");
            }
            else
            {
                row.Add("\"\""); // Empty name if same as ID
            }

            // Get build stats from BuildTracker
            var buildStats = BuildTracker.GetPlayerStats(playerId);

            // Build stats
            row.Add(buildStats.TotalBuildsPlaced.ToString());
            row.Add(buildStats.WoodBuilds.ToString());
            row.Add(buildStats.StoneBuilds.ToString());
            row.Add(buildStats.MetalBuilds.ToString());
            row.Add(buildStats.WallsPlaced.ToString());
            row.Add(buildStats.FloorsPlaced.ToString());
            row.Add(buildStats.StairsPlaced.ToString());
            row.Add(buildStats.RoofsPlaced.ToString());
            row.Add(buildStats.BuildsEdited.ToString());
            row.Add(buildStats.BuildsDestroyed.ToString());

            // Combat stats from DamageTracker
            var damageStats = DamageTracker.PlayerStats.ContainsKey(playerId)
                ? DamageTracker.PlayerStats[playerId]
                : null;

            row.Add(damageStats?.Eliminations.ToString() ?? "0");
            row.Add(damageStats?.TotalDamageDealt.ToString() ?? "0");
            row.Add(damageStats?.TotalDamageTaken.ToString() ?? "0");
            row.Add(damageStats?.ShotsHit.ToString() ?? "0");

            // Harvest stats from HarvestTracker (only harvesting, not collection)
            var harvestStats = HarvestTracker.GetPlayerStats(playerId);
            row.Add(harvestStats.TotalMaterialsHarvested.ToString());
            row.Add(harvestStats.WoodHarvested.ToString());
            row.Add(harvestStats.StoneHarvested.ToString());
            row.Add(harvestStats.MetalHarvested.ToString());
            row.Add(harvestStats.HarvestActions.ToString());

            csvLines.Add(string.Join(",", row));
        }

        await File.WriteAllLinesAsync(filePath, csvLines);
        Console.WriteLine($"\n*** CSV exported to: {filePath} ***");
        Console.WriteLine($"*** Exported {allPlayerIds.Count} unique players with {headers.Count} columns ***");
    }

    private string GetPlayerNameFromReplay(string playerId)
    {
        // Look for player in the builder's player data
        var playerData = Builder.GetAllPlayers().FirstOrDefault(p => p.PlayerId == playerId);

        if (playerData != null && !string.IsNullOrEmpty(playerData.PlayerName))
        {
            return playerData.PlayerName;
        }

        // Fallback to Epic ID if no display name found
        return playerId;
    }


    private string GetStatValue(string playerId, string statName)
    {
        if (playerRealtimeStats.ContainsKey(playerId) &&
            playerRealtimeStats[playerId].ContainsKey(statName))
        {
            return playerRealtimeStats[playerId][statName].ToString() ?? "0";
        }
        return "0";
    }


}