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
using FortniteReplayReader.Models.Eliminations;
using Unreal.Encryption;
using System.Collections;
using FortniteReplayReader.Models.NetFieldExports.Buildings;
using FortniteReplayReader.Models.NetFieldExports.Builds;
using FortniteReplayReader.Models.NetFieldExports.Vehicles;

namespace FortniteReplayReader;


public class ReplayReader : Unreal.Core.ReplayReader<FortniteReplay>
{


    private class UnresolvedHarvestHit
    {
        public uint OwnerActorGuid { get; set; }
        public uint ChannelIndex { get; set; }
        public string MaterialType { get; set; }
        public string ObjectPath { get; set; }
        public float GameTime { get; set; }
    }

    private List<UnresolvedHarvestHit> unresolvedHarvestHits = new List<UnresolvedHarvestHit>();

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
    private uint mostRecentActiveChannel = 0;


    private Dictionary<uint, float> lastChannelActivityTime = new Dictionary<uint, float>();


    private Dictionary<uint, PlayerInfo> pawnToPlayerState = new Dictionary<uint, PlayerInfo>();

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

    private Dictionary<short, string> _ownerPersistentIdToEpicId = new();
    private BuildOwnershipTracker _buildOwnershipTracker = new();

    private BuildAttributionTracker _buildAttributionTracker = new();
    private Dictionary<string, string> playerNames = new Dictionary<string, string>();
    private FortniteReplayBuilder Builder;
    private HashSet<string> processedBuilds = new();
    private HashSet<string> processedEliminationIds = new HashSet<string>();

    private StreamWriter elimLogWriter;
    private string elimLogPath = "elimination_debug.log";

    private ElimTracker ElimTracker = new ElimTracker();

    // CORRECT - at class level with explicit types
    private Dictionary<string, string> activeKnocks = new Dictionary<string, string>();
    private Dictionary<string, int> eliminationCounts = new Dictionary<string, int>();


    private Dictionary<string, string> playerIdToEpicId = new(); // Internal PlayerId -> Epic Account ID

    // Channel and export analysis
    private Dictionary<uint, string> channelToPlayerId = new Dictionary<uint, string>();
    private Dictionary<uint, string> channelTypes = new Dictionary<uint, string>();
    private Dictionary<string, List<uint>> exportTypeToChannels = new Dictionary<string, List<uint>>();
    private Dictionary<uint, uint> channelToActorGuid = new Dictionary<uint, uint>();
    private Dictionary<uint, List<string>> channelExportHistory = new Dictionary<uint, List<string>>();

    private Dictionary<string, HashSet<string>> eliminationPropertiesFound = new Dictionary<string, HashSet<string>>();
    private List<Dictionary<string, object>> eliminationSamples = new List<Dictionary<string, object>>();

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

    private uint GetPlayerEliminations(string playerId)
    {
        if (string.IsNullOrEmpty(playerId))
            return 0;

        try
        {
            var playerData = Builder.GetAllPlayers()
                .FirstOrDefault(p => p.PlayerId.Equals(playerId, StringComparison.OrdinalIgnoreCase));

            return playerData?.Kills ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    private string GetPlayerName(string playerId)
    {
        if (string.IsNullOrEmpty(playerId))
            return "Unknown";

        // Method 1: Look up in Builder's player data (most reliable)
        try
        {
            var playerData = Builder.GetAllPlayers()
                .FirstOrDefault(p => p.PlayerId.Equals(playerId, StringComparison.OrdinalIgnoreCase));

            if (playerData != null && !string.IsNullOrEmpty(playerData.PlayerName))
            {
                return playerData.PlayerName;
            }
        }
        catch { }

        // Method 2: Try playerNames dictionary (if manually populated)
        if (playerNames.TryGetValue(playerId, out var name))
        {
            return name;
        }


        // Fallback: Return shortened ID
        return playerId.Length > 8
            ? playerId.Substring(0, 8) + "..."
            : playerId;
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
            LogVerbose("\n✅ All harvestables in this replay are known!");
            return;
        }

        LogVerbose("\n=== UNKNOWN HARVESTABLES REPORT ===");
        LogVerbose($"Found {unknownHarvestables.Count} unknown harvestable types\n");

        var sorted = unknownHarvestables.Values
            .OrderByDescending(x => x.HitCount)
            .ToList();

        var report = new List<string>();
        report.Add("BlueprintClass,HitCount,UniquePlayersHit,EstimatedMaterialType,ArchetypePath,FirstHitTime,LastHitTime");

        LogVerbose("Top Unknown Harvestables:");
        LogVerbose("─────────────────────────────────────────────────────────");

        foreach (var unknown in sorted.Take(20))
        {
            var firstHit = unknown.HitTimestamps.Min();
            var lastHit = unknown.HitTimestamps.Max();

            LogVerbose($"\n{unknown.BlueprintClass}");
            LogVerbose($"  Hit Count: {unknown.HitCount}");
            LogVerbose($"  Unique Players: {unknown.HitByPlayers.Count}");
            LogVerbose($"  Estimated Type: {unknown.EstimatedMaterialType}");
            LogVerbose($"  First Hit: {firstHit:F1}s, Last Hit: {lastHit:F1}s");

            report.Add($"\"{unknown.BlueprintClass}\",{unknown.HitCount},{unknown.HitByPlayers.Count},\"{unknown.EstimatedMaterialType}\",\"{unknown.ArchetypePath}\",{firstHit:F1},{lastHit:F1}");
        }

        File.WriteAllLines(outputPath, report);
        LogVerbose($"\n✅ Unknown harvestables exported to: {outputPath}");
        LogVerbose($"   Total unknown types: {unknownHarvestables.Count}");
        LogVerbose($"   Total hits on unknowns: {unknownHarvestables.Values.Sum(x => x.HitCount)}");
    }


    public void SetEpicAccessToken(string accessToken)
    {
        epicAccessToken = accessToken;
    }


    private void LogElimination(string message)
    {
        try
        {
            elimLogWriter?.WriteLine(message);
        }
        catch { /* Ignore if can't write */ }
    }


    public ReplayReader(ILogger? logger = null, ParseMode parseMode = ParseMode.Debug) : base(logger, parseMode)
    {
        Builder = new FortniteReplayBuilder();
        DamageTracker = new DamageTracker();
        BuildTracker = new BuildTracker();

        var databasePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FortniteHarvestables_v2.json");
        harvestableDatabase.Load(databasePath);
        harvestableDatabase.PrintStatistics();

        try
        {
            elimLogWriter = new StreamWriter(elimLogPath, append: false) { AutoFlush = true };
            elimLogWriter.WriteLine($"=== ELIMINATION DEBUG LOG ===");
            elimLogWriter.WriteLine($"Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            elimLogWriter.WriteLine();
        }
        catch (Exception ex)
        {
            //LogVerbose($"Warning: Could not create elimination log: {ex.Message}");
        }


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

    private Dictionary<string, int> ExtractFinalAthenaKills()
    {
        var finalKills = new Dictionary<string, int>();

        foreach (var player in playerRealtimeStats)
        {
            if (player.Value.ContainsKey("AthenaKills"))
            {
                if (int.TryParse(player.Value["AthenaKills"].ToString(), out int kills))
                {
                    finalKills[player.Key] = kills;
                }
            }
        }

        LogVerbose("\n=== FINAL ATHENAKILLS (from replay) ===");
        foreach (var (playerId, kills) in finalKills.OrderByDescending(x => x.Value))
        {
            var playerName = GetPlayerNameFromReplay(playerId);
            LogVerbose($"{playerName}: {kills}");
        }

        return finalKills;
    }

    private void ExtractAndPrintFinalAthenaKills()
    {
        LogVerbose($"\n=== ATHENAKILLS COVERAGE ANALYSIS ===");
        LogVerbose($"Total players in replay: {playerRealtimeStats.Count}");

        var withAthenaKills = playerRealtimeStats.Where(p => p.Value.ContainsKey("AthenaKills")).ToList();
        var withoutAthenaKills = playerRealtimeStats.Where(p => !p.Value.ContainsKey("AthenaKills")).ToList();

        LogVerbose($"Players WITH AthenaKills stat: {withAthenaKills.Count}");
        LogVerbose($"Players WITHOUT AthenaKills stat: {withoutAthenaKills.Count}");

        LogVerbose($"\nPlayers missing AthenaKills:");
        foreach (var player in withoutAthenaKills.Take(20))
        {
            LogVerbose($"  - {GetPlayerNameFromReplay(player.Key)}");
        }
    }


    public void ResolveBroadcastChannels()
    {
        Console.WriteLine("\n" + new string('=', 80));
        Console.WriteLine("🔗 RESOLVING BROADCAST CHANNELS");
        Console.WriteLine(new string('=', 80));

        int resolved = 0;

        foreach (var broadcastCh in broadcastChannels)
        {
            string playerId = "";

            // Try Method 1: Direct Builder lookup
            playerId = Builder.GetPlayerIdFromAnyChannel(broadcastCh);

            // Try Method 2: Owner GUID → Player
            if (string.IsNullOrEmpty(playerId) && broadcastChannelToOwnerGuid.TryGetValue(broadcastCh, out var ownerGuid))
            {
                Console.WriteLine($"   DEBUG: Channel {broadcastCh} has Owner GUID {ownerGuid}");
                if (actorGuidToPlayerId.TryGetValue(ownerGuid, out var ownerPlayerId))
                {
                    playerId = ownerPlayerId;
                    Console.WriteLine($"   DEBUG: Owner GUID {ownerGuid} → Player {ownerPlayerId}");
                }
                else
                {
                    Console.WriteLine($"   DEBUG: Owner GUID {ownerGuid} NOT in actorGuidToPlayerId");
                }
            }
            else if (string.IsNullOrEmpty(playerId))
            {
                Console.WriteLine($"   DEBUG: Channel {broadcastCh} has NO Owner GUID stored");
            }

            if (!string.IsNullOrEmpty(playerId))
            {
                Builder.LinkBroadcastChannelToPlayer(broadcastCh, playerId);
                channelToPlayerId[broadcastCh] = playerId;
                resolved++;
                Console.WriteLine($"✅ Channel {broadcastCh} → {playerId}");
            }
        }

        Console.WriteLine($"\n📊 Resolved: {resolved}/{broadcastChannels.Count}");
        Console.WriteLine(new string('=', 80) + "\n");
    }


    public FortniteReplay ReadReplay(Stream stream)
    {
        using var archive = new Unreal.Core.BinaryReader(stream);

        Builder = new FortniteReplayBuilder();
        DamageTracker = new DamageTracker();

        try
        {
            LogVerbose("[READREPLAY_START]");
            ReadReplay(archive);
            LogVerbose("[READREPLAY_COMPLETE]");
        }
        catch (Exception ex)
        {
            LogVerbose($"[READREPLAY_CRASH] {ex.Message}");
            LogVerbose($"CRASH during replay reading: {ex}");
            LogVerbose($"Stack: {ex.StackTrace}");
            throw;
        }

        LogVerbose("[BUILDING_REPLAY]");
        var replay = Builder.Build(Replay);
        replay.DamageSummary = DamageTracker.GetDamageSummary();

        LogVerbose("[POSTPROCESSING_HARVESTS]");
        ResolveBroadcastChannels();
        PostProcessHarvestHits();

        LogVerbose("[UPDATING_KILLFEED]");
        Builder.UpdateKillFeedWithNames();
        Builder.SetPlayerInfo(playerInfoByPlayerId);
        PrintEliminationsSummaryWithNames();

        LogVerbose("[EVALUATING_ELIMINATIONS]");
        var elimCredits = EvaluateEliminationsFromEvents();
        Builder.SetEliminationCredits(elimCredits);

        LogVerbose($"[DEBUG] Total builds tracked by OwnerPersistentID: {_buildCountByOwnerPersistentId.Count}");
        LogVerbose($"[DEBUG] Total unique OwnerPersistentIDs: {_buildCountByOwnerPersistentId.Keys.Count}");

        LogVerbose("[PRINTING_ANALYSIS]");
        PrintStructuralAnalysis();
        PrintEventSummary();

        LogVerbose("[PRINTING_SUMMARIES]");
        BuildTracker.PrintBuildSummary();
        BuildTracker.PrintSanityChecks();
        BuildTracker.PrintPlayerIdMappings();
        HarvestTracker.PrintHarvestSummary();

        LogVerbose("[BEFORE_LOGBUILDCOUNTS]");
        LogBuildCountsByOwner();
        MapPlayerStatesForBuildTracking();
        LogVerbose("[AFTER_LOGBUILDCOUNTS]");

        LogVerbose("[EXPORTING_UNKNOWNS]");
        var unknownHarvestablesPath = "unknown_harvestables.csv";
        ExportUnknownHarvestables(unknownHarvestablesPath);

        LogVerbose("[READREPLAY_FINISHED]");
        return replay;
    }


    private List<(string eliminatedId, string eliminatorId, double time, bool knocked)> _parsedEliminations = new();


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

    public void PrintEliminationsSummaryWithNames()
    {
        LogVerbose($"\n≡ƒôê ELIMINATION EVENTS SUMMARY:");
        LogVerbose($"   Total eliminations parsed: {_parsedEliminations.Count}\n");

        for (int i = 0; i < _parsedEliminations.Count; i++)
        {
            var (eliminatedId, eliminatorId, time, knocked) = _parsedEliminations[i];
            string action = knocked ? "KNOCK" : "ELIM";

            // Map IDs to display names
            var eliminatedName = GetPlayerNameFromReplay(eliminatedId) ?? eliminatedId;
            var eliminatorName = GetPlayerNameFromReplay(eliminatorId) ?? eliminatorId;

            LogVerbose($"   [{i + 1}] {action} @ {time:F2}s: {eliminatedName} by {eliminatorName}");
        }

        LogVerbose($"\n   ✅ Summary complete\n");
    }

    private Dictionary<string, List<double>> GetPlayerReboots()
    {
        var playerReboots = new Dictionary<string, List<double>>();

        var playerStateHistory = Builder.GetPlayerStateHistory();

        foreach (var (playerId, history) in playerStateHistory)
        {
            playerReboots[playerId] = new List<double>();
            var sortedHistory = history.OrderBy(h => h.timestamp).ToList();

            bool lastChipState = true;

            foreach (var (timestamp, property, value) in sortedHistory)
            {
                if (property == "bResurrectionChipAvailable" && value is bool chipValue)
                {
                    // Chip goes from true -> false = reboot used
                    if (lastChipState && !chipValue)
                    {
                        playerReboots[playerId].Add(timestamp);
                    }
                    lastChipState = chipValue;
                }
            }
        }

        return playerReboots;
    }


    /// <summary>
    /// Evaluate eliminations based on authoritative elimination events
    /// Track who knocked whom, credit knockers if their teammate is alive at elimination time
    /// </summary>
    /// <summary>
    /// Evaluate eliminations with complete Fortnite elimination credit logic
    /// Clears stale knockers when players are revived
    /// </summary>
    public Dictionary<string, int> EvaluateEliminationsFromEvents()
    {
        LogVerbose($"\n≡ƒôì EVALUATING ELIMINATIONS FROM EVENTS (WITH STALE KNOCKER CLEARING):");
        LogVerbose($"   Total eliminations to process: {_parsedEliminations.Count}\n");

        var mostRecentKnocker = new Dictionary<string, (string knockerId, double knockTime)>();
        var playerEliminatedTime = new Dictionary<string, double>();
        var playerRevives = GetPlayerRevivesAndReboots();
        var playerTeams = BuildPlayerTeamMap();
        var playerReboots = GetValidatedReboots();
        var eliminationCredits = new Dictionary<string, int>();

        int creditedCount = 0;
        int noKnockerCount = 0;
        int teamDeadCount = 0;
        int revivedCount = 0;
        int noKnockerCreditedCount = 0;

        int processedCount = 0;

        foreach (var (eliminatedId, eliminatorId, time, knocked) in _parsedEliminations)
        {
            processedCount++;

            if (knocked)
            {
                // NEW: Ignore self-knockdowns (player knocked themselves)
                if (eliminatorId == eliminatedId)
                {
                    LogVerbose($"   [{processedCount}] SELF-KNOCK @ {time:F2}s: {GetPlayerNameFromReplay(eliminatedId)} (IGNORED - thirsted finish will get credit)");
                    continue;  // Skip self-knocks, don't store as a valid knocker
                }

                // 🔴 KNOCKDOWN EVENT: Store this as the most recent knocker
                mostRecentKnocker[eliminatedId] = (eliminatorId, time);
                LogVerbose($"   [{processedCount}] KNOCK @ {time:F2}s: {GetPlayerNameFromReplay(eliminatedId)} by {GetPlayerNameFromReplay(eliminatorId)}");
            }
            else
            {
                // 🟢 ELIMINATION EVENT: Evaluate for credit
                LogVerbose($"   [{processedCount}] ELIM @ {time:F2}s: {GetPlayerNameFromReplay(eliminatedId)} by {GetPlayerNameFromReplay(eliminatorId)}");

                playerEliminatedTime[eliminatedId] = time;

                // NEW: Clear stale knockers (knocked before a revive)
                if (mostRecentKnocker.ContainsKey(eliminatedId) && playerRevives.ContainsKey(eliminatedId))
                {
                    var (knockerId, knockTime) = mostRecentKnocker[eliminatedId];
                    var reviveTimes = playerRevives[eliminatedId];

                    // If there's a revive after the knock but before this elim, clear the knocker
                    var reviveAfterKnock = reviveTimes.FirstOrDefault(reviveTime => reviveTime > knockTime && reviveTime < time);
                    if (reviveAfterKnock > 0)
                    {
                        LogVerbose($"      Cleared stale knocker: {GetPlayerNameFromReplay(knockerId)} @ {knockTime:F2}s (revived @ {reviveAfterKnock:F2}s)");
                        mostRecentKnocker.Remove(eliminatedId);
                    }
                }

                if (mostRecentKnocker.ContainsKey(eliminatedId))
                {
                    var (knockerId, knockTime) = mostRecentKnocker[eliminatedId];
                    var knockerName = GetPlayerNameFromReplay(knockerId);
                    var knockerTeam = playerTeams.ContainsKey(knockerId) ? playerTeams[knockerId] : -1;

                    LogVerbose($"      → Knocker: {knockerName} (Team {knockerTeam}) @ {knockTime:F2}s");

                    // Check if victim was revived between knock and elimination
                    bool wasRevived = playerRevives.ContainsKey(eliminatedId) &&
                        playerRevives[eliminatedId].Any(reviveTime => reviveTime > knockTime && reviveTime < time);

                    if (wasRevived)
                    {
                        LogVerbose($"      ❌ REVIVED between knock ({knockTime:F2}s) and elim ({time:F2}s)");
                        revivedCount++;
                        mostRecentKnocker.Remove(eliminatedId);
                    }
                    else
                    {
                        // Check if knocker's team is alive at elimination time
                        bool teamAlive = IsTeammateAliveAtTime(knockerTeam, time, playerTeams, playerEliminatedTime, playerRevives);

                        // Check: if knocker was DEAD before the elimination AND rebooted AFTER, don't credit
                        bool knockerWasDeadBeforeElim = playerEliminatedTime.ContainsKey(knockerId) &&
                                                        playerEliminatedTime[knockerId] < time;
                        bool knockerRebooted = playerReboots.ContainsKey(knockerId) &&
                            playerReboots[knockerId].Any(rebootTime => rebootTime > time);

                        if (knockerWasDeadBeforeElim && knockerRebooted)
                        {
                            LogVerbose($"      ❌ NO CREDIT - Knocker was dead and rebooted AFTER elimination");
                            teamDeadCount++;
                        }
                        else if (knockerTeam >= 0 && teamAlive)
                        {
                            LogVerbose($"      ✅ CREDIT awarded to {knockerName} (team alive)");
                            creditedCount++;

                            if (!eliminationCredits.ContainsKey(knockerId))
                                eliminationCredits[knockerId] = 0;
                            eliminationCredits[knockerId]++;
                        }
                        else
                        {
                            LogVerbose($"      ❌ NO CREDIT - Knocker's team dead at {time:F2}s");
                            teamDeadCount++;
                        }

                        mostRecentKnocker.Remove(eliminatedId);
                    }
                }
                else
                {
                    // NO KNOCKER ON RECORD - Check if eliminator's team is alive
                    var eliminatorTeam = playerTeams.ContainsKey(eliminatorId) ? playerTeams[eliminatorId] : -1;
                    bool eliminatorTeamAlive = IsTeammateAliveAtTime(eliminatorTeam, time, playerTeams, playerEliminatedTime, playerRevives);

                    LogVerbose($"      → Eliminator: {GetPlayerNameFromReplay(eliminatorId)} (Team {eliminatorTeam})");

                    if (eliminatorTeam >= 0 && eliminatorTeamAlive)
                    {
                        LogVerbose($"      ✅ CREDIT awarded to {GetPlayerNameFromReplay(eliminatorId)} (direct elim, team alive)");
                        noKnockerCreditedCount++;

                        if (!eliminationCredits.ContainsKey(eliminatorId))
                            eliminationCredits[eliminatorId] = 0;
                        eliminationCredits[eliminatorId]++;
                    }
                    else
                    {
                        LogVerbose($"      ❌ NO CREDIT - No knocker and eliminator's team dead");
                        noKnockerCount++;
                    }
                }

                LogVerbose("");
            }
        }

        LogVerbose($"\n📊 ELIMINATION BREAKDOWN:");
        LogVerbose($"   Total eliminations: {_parsedEliminations.Count / 2}");
        LogVerbose($"   Credited (knocker): {creditedCount}");
        LogVerbose($"   Credited (direct elim): {noKnockerCreditedCount}");
        LogVerbose($"   Total credited: {creditedCount + noKnockerCreditedCount}");
        LogVerbose($"   ---");
        LogVerbose($"   No knocker (no credit): {noKnockerCount}");
        LogVerbose($"   Team dead at elim: {teamDeadCount}");
        LogVerbose($"   Revived between knock and elim: {revivedCount}");
        LogVerbose("");

        PrintFinalCredits(eliminationCredits);
        return eliminationCredits;
    }



    private Dictionary<string, List<double>> GetValidatedReboots()
    {
        var playerReboots = new Dictionary<string, List<double>>();

        var playerStateHistory = Builder.GetPlayerStateHistory();

        foreach (var (playerId, history) in playerStateHistory)
        {
            playerReboots[playerId] = new List<double>();
            var sortedHistory = history.OrderBy(h => h.timestamp).ToList();

            bool lastChipState = true;
            bool wasJustFinished = false;

            foreach (var (timestamp, property, value) in sortedHistory)
            {
                // Track when player is finished (eliminated)
                if (property == "bDBNO" && value is bool bdbno)
                {
                    if (bdbno == false)
                    {
                        var deathCauseEntry = sortedHistory
                            .Where(h => h.timestamp == timestamp && h.property == "DeathCause")
                            .FirstOrDefault();
                        int deathCause = deathCauseEntry.value is int ? (int) deathCauseEntry.value : 50;

                        if (deathCause != 50) // Not a revive
                            wasJustFinished = true;
                    }
                }

                // Track chip usage for reboot
                if (property == "bResurrectionChipAvailable" && value is bool chipValue)
                {
                    if (wasJustFinished && lastChipState && !chipValue)
                    {
                        playerReboots[playerId].Add(timestamp);
                    }
                    lastChipState = chipValue;
                }
            }
        }

        return playerReboots;
    }

    /// <summary>
    /// Check if any teammate of knocker is alive at elimination time
    /// </summary>
    private bool IsTeammateAliveAtTime(int teamIndex, double elimTime,
    Dictionary<string, int> playerTeams,
    Dictionary<string, double> playerEliminatedTime,
    Dictionary<string, List<double>> playerRevives)
    {
        if (teamIndex < 0) return false;

        // Find all players on this team
        var teammates = playerTeams.Where(p => p.Value == teamIndex).Select(p => p.Key).ToList();

        foreach (var teammate in teammates)
        {
            bool alive = true;

            // If teammate has been eliminated, check if it was after elimTime
            if (playerEliminatedTime.ContainsKey(teammate))
            {
                double elimmedTime = playerEliminatedTime[teammate];
                if (elimmedTime <= elimTime)
                {
                    // Teammate was eliminated BEFORE or AT the same time - not alive
                    // UNLESS they were revived after elimination
                    bool revived = playerRevives.ContainsKey(teammate) &&
                        playerRevives[teammate].Any(reviveTime => reviveTime > elimmedTime && reviveTime < elimTime);

                    alive = revived;
                }
            }

            if (alive) return true;  // At least one teammate alive
        }

        return false;  // No teammates alive
    }
    /// <summary>
    /// Build map of player -> team index
    /// </summary>
    private Dictionary<string, int> BuildPlayerTeamMap()
    {
        var playerTeams = new Dictionary<string, int>();

        // Get players from the replay
        var allPlayers = Builder.GetPlayers();

        LogVerbose($"\n🔍 DEBUG: Building player team map");
        LogVerbose($"   Total players: {allPlayers.Count}");

        foreach (var kvp in allPlayers)
        {
            var playerData = kvp.Value;

            if (playerData.PlayerId != null && playerData.TeamIndex.HasValue)
            {
                playerTeams[playerData.PlayerId] = playerData.TeamIndex.Value;
                LogVerbose($"   {GetPlayerNameFromReplay(playerData.PlayerId)}: Team {playerData.TeamIndex}");
            }
        }

        LogVerbose($"   Total players with teams: {playerTeams.Count}\n");
        return playerTeams;
    }

    /// <summary>
    /// Get revive and reboot times for each player
    /// Returns: playerId -> List of revive times
    /// </summary>
    private Dictionary<string, List<double>> GetPlayerRevivesAndReboots()
    {
        var playerRevives = new Dictionary<string, List<double>>();

        var playerStateHistory = Builder.GetPlayerStateHistory();

        foreach (var (playerId, history) in playerStateHistory)
        {
            playerRevives[playerId] = new List<double>();
            var sortedHistory = history.OrderBy(h => h.timestamp).ToList();

            for (int i = 0; i < sortedHistory.Count; i++)
            {
                var (timestamp, property, value) = sortedHistory[i];

                // Check for revive: bDBNO = false with DeathCause = 50 (Unspecified) means revived
                if (property == "bDBNO" && value is bool bdbno && bdbno == false)
                {
                    var deathCauseEntry = sortedHistory
                        .Where(h => h.timestamp == timestamp && h.property == "DeathCause")
                        .FirstOrDefault();

                    int deathCause = deathCauseEntry.value is int ? (int) deathCauseEntry.value : 50;

                    if (deathCause == 50)  // Revive
                    {
                        playerRevives[playerId].Add(timestamp);
                    }
                }

                // Check for reboot: bResurrectionChipAvailable goes from true to false
                if (property == "bResurrectionChipAvailable" && value is bool chipValue && !chipValue)
                {
                    // Look for previous true value
                    var prevChipState = sortedHistory
                        .Where(h => h.timestamp < timestamp && h.property == "bResurrectionChipAvailable")
                        .OrderByDescending(h => h.timestamp)
                        .FirstOrDefault();

                    if (prevChipState.property != null && prevChipState.value is bool prevChip && prevChip)
                    {
                        playerRevives[playerId].Add(timestamp);
                    }
                }
            }
        }

        return playerRevives;
    }

    /// <summary>
    /// Print final elimination credits
    /// </summary>
    private void PrintFinalCredits(Dictionary<string, int> eliminationCredits)
    {
        LogVerbose($"\n≡ƒôê FINAL ELIMINATION CREDITS:");
        LogVerbose($"   Total credits awarded: {eliminationCredits.Values.Sum()}\n");

        var sortedCredits = eliminationCredits
            .OrderByDescending(k => k.Value)
            .ThenBy(k => GetPlayerNameFromReplay(k.Key))
            .ToList();

        foreach (var (playerId, count) in sortedCredits)
        {
            var playerName = GetPlayerNameFromReplay(playerId);
            LogVerbose($"   {playerName}: {count} eliminations");
        }

        LogVerbose("");
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
    private Dictionary<uint, List<(double timestamp, string playerId)>> channelOwnershipHistory = new();

    private Dictionary<uint, uint> broadcastChannelToOwnerGuid = new();

    private HashSet<uint> openedChannels = new();
    private Dictionary<uint, uint> allChannelsToActorGuid = new();
    protected override void OnChannelOpened(uint channelIndex, NetworkGUID? actorGuid)
    {
        openedChannels.Add(channelIndex);

        var playerId = Builder.GetPlayerIdFromAnyChannel(channelIndex);

        // ✅ DEBUG: Log every channel that opens
        if (_netGuidCache.TryGetPathName(actorGuid.Value, out var path))
        {
            Console.WriteLine($"📍 [CHANNEL_OPENED] Channel {channelIndex}: Actor={actorGuid.Value} ({path}) → Player {playerId}");
        }
        else
        {
            Console.WriteLine($"📍 [CHANNEL_OPENED] Channel {channelIndex}: Actor={actorGuid.Value} (unknown) → Player {playerId}");
        }

        if (actorGuid.Value == 13374)
        {
            Console.WriteLine($"🔴 [FOUND_13374] Channel {channelIndex}: Actor=13374 is opening!");
        }



        if (actorGuid.Value != 0 && Channels[channelIndex]?.Actor != null)
        {
            var actor = Channels[channelIndex].Actor;
            var actorGuidValue = actorGuid.Value;

            if (actor.Archetype != null)
            {
                var archetypeGuidValue = actor.Archetype.Value;

                if (_netGuidCache.TryGetPathName(archetypeGuidValue, out var path__))
                {
                    // Log ALL archetypes so we can find the broadcast ones
                    Console.WriteLine($"[ON CHANNEL OPENED] Channel={channelIndex}, ActorGUID={actorGuidValue}, Archetype={path__}");
                }
            }
        }

        base.OnChannelOpened(channelIndex, actorGuid);

        if (actorGuid != null && Channels[channelIndex]?.Actor != null)
        {
            var actor = Channels[channelIndex].Actor;
            var actorGuidValue = actorGuid.Value;
            var currentTime = Builder.GetCurrentTimeDouble();

            // ✅ Track ALL channels to their actor GUIDs
            allChannelsToActorGuid[channelIndex] = actorGuidValue;

            // ✅ NEW: Register this actor in the builder for harvest lookup
            Builder.RegisterActorGuid(actorGuidValue, channelIndex, actor);

            if (actor.Archetype != null)
            {
                var archetypeGuidValue = actor.Archetype.Value;
                actorGuidToArchetypeGuid[actorGuidValue] = archetypeGuidValue;

                string archetypePath;
                if (_netGuidCache.TryGetPathName(archetypeGuidValue, out var path__))
                {
                    archetypeGuidToPath[archetypeGuidValue] = path__;
                    archetypePath = path__;

                    // ✅ Extract and register material from archetype path
                    string material = "Stone"; // default
                    if (path__.Contains("Wood") || path__.Contains("Tree"))
                        material = "Wood";
                    else if (path__.Contains("Metal") || path__.Contains("Car") || path__.Contains("Machinery"))
                        material = "Metal";

                    Builder.RegisterArchetypeMaterial(archetypeGuidValue, material);
                    Console.WriteLine($"🌲 Registered archetype {archetypeGuidValue}: {material} from {path__}");
                }
                else
                {
                    archetypePath = $"ArchetypeGUID={archetypeGuidValue} (path pending)";
                }

                Console.WriteLine($"🌲 Actor spawned: ActorGUID={actorGuidValue}, Archetype={archetypePath}");

                // ✅ ALWAYS tell the builder about this channel-actor mapping
                Builder.AddActorChannel(channelIndex, actorGuidValue);
                LogVerbose($"    ↳ Builder notified: Channel {channelIndex} ↔ Actor {actorGuidValue}");

                // Track FortPlayerStateAthena
                // Track FortPlayerStateAthena
                if (archetypePath.Contains("FortPlayerStateAthena"))
                {
                    // ✅ Use the actual PlayerId from the actor
                    var actualPlayerId = ""; // Will be set when we process the replication data

                    if (!actorGuidToPlayerId.ContainsKey(actorGuidValue))
                    {
                        // Create mapping with actor GUID as key
                        // We'll update with actual PlayerId when replication data arrives
                        actorGuidToPlayerId[actorGuidValue] = $"PlayerState_{actorGuidValue}";

                        playerInfoByPlayerId[$"PlayerState_{actorGuidValue}"] = new PlayerInfo(
                            $"PlayerState_{actorGuidValue}",
                            channelIndex,
                            actorGuidValue,
                            $"PlayerState_{actorGuidValue}",
                            actorGuidValue
                        );

                        // ✅ Map this channel to the player
                        channelToPlayerId[channelIndex] = $"PlayerState_{actorGuidValue}";

                        // ✅ Track channel ownership history
                        if (!channelOwnershipHistory.ContainsKey(channelIndex))
                            channelOwnershipHistory[channelIndex] = new List<(double, string)>();
                        channelOwnershipHistory[channelIndex].Add((currentTime, $"PlayerState_{actorGuidValue}"));

                        LogVerbose($"    ↳ Registered PlayerState {actorGuidValue} on channel {channelIndex}");
                    }
                }

                // Track FortPawnAthena
                if (archetypePath.Contains("FortPawnAthena") || archetypePath.Contains("PlayerPawn"))
                {
                    channelToActorGuid[channelIndex] = actorGuidValue;

                    // ✅ Try to map pawn channel to player immediately
                    if (actorGuidToPlayerId.TryGetValue(actorGuidValue, out var playerId_))
                    {
                        channelToPlayerId[channelIndex] = playerId_;

                        // ✅ Track channel ownership history
                        if (!channelOwnershipHistory.ContainsKey(channelIndex))
                            channelOwnershipHistory[channelIndex] = new List<(double, string)>();
                        channelOwnershipHistory[channelIndex].Add((currentTime, playerId_));

                        LogVerbose($"    ↳ Mapped pawn channel {channelIndex} → player {playerId_} at {currentTime}s");
                    }
                    else
                    {
                        LogVerbose($"    ↳ Tracked pawn channel {channelIndex} → ActorGUID {actorGuidValue} (player TBD)");
                    }
                }

                // ✅ NEW: Handle FortBroadcastRemoteClientInfo
                // ✅ NEW: Handle FortBroadcastRemoteClientInfo
                // ✅ NEW: Handle FortBroadcastRemoteClientInfo
                if (archetypePath.Contains("FortBroadcastRemoteClientInfo"))
                {
                    broadcastChannels.Add(channelIndex);
                    Console.WriteLine($"📡 BROADCAST Channel={channelIndex}, ActorGUID={actorGuidValue}");
                }
            }
        }
    }
    private string? ResolvePlayerFromPawn(uint pawnActorGuid)
    {
        // Method 1: Direct mapping (if we already mapped this pawn)
        if (actorGuidToPlayerId.TryGetValue(pawnActorGuid, out var directPlayerId))
        {
            LogVerbose($"    ✓ Method 1: Direct mapping found");
            return directPlayerId;
        }

        // Method 2: Check pawnToPlayerState mapping
        if (pawnToPlayerState.TryGetValue(pawnActorGuid, out var playerInfo))
        {
            LogVerbose($"    ✓ Method 2: Pawn→PlayerState mapping found");
            return playerInfo?.PlayerId;
        }

        // Method 3: Try nearby actor GUIDs (±10 range)
        // This handles cases where the pawn GUID is close to the player state GUID
        for (int offset = 1; offset <= 10; offset++)
        {
            // Try higher
            if (actorGuidToPlayerId.TryGetValue(pawnActorGuid + (uint) offset, out var higherPlayerId))
            {
                LogVerbose($"    ✓ Method 3: Found nearby actor +{offset} ({pawnActorGuid + offset})");
                // Cache this mapping for future use
                actorGuidToPlayerId[pawnActorGuid] = higherPlayerId;
                return higherPlayerId;
            }

            // Try lower
            if (pawnActorGuid >= offset &&
                actorGuidToPlayerId.TryGetValue(pawnActorGuid - (uint) offset, out var lowerPlayerId))
            {
                LogVerbose($"    ✓ Method 3: Found nearby actor -{offset} ({pawnActorGuid - offset})");
                // Cache this mapping for future use
                actorGuidToPlayerId[pawnActorGuid] = lowerPlayerId;
                return lowerPlayerId;
            }
        }

        // Method 4: Check if this actor GUID has an archetype that's a pawn
        if (actorGuidToArchetypeGuid.TryGetValue(pawnActorGuid, out var archetypeGuid))
        {
            if (archetypeGuidToPath.TryGetValue(archetypeGuid, out var archetypePath))
            {
                if (archetypePath.Contains("Pawn"))
                {
                    LogVerbose($"    ℹ️ Method 4: Confirmed this is a pawn archetype: {archetypePath}");
                    // Try to find the owning player state by looking at recent player states
                    var recentPlayer = playerInfoByPlayerId.Values
                        .OrderByDescending(p => p.ActorGuid)
                        .FirstOrDefault(p => p.ActorGuid < pawnActorGuid);

                    if (recentPlayer != null)
                    {
                        LogVerbose($"    ✓ Method 4: Using most recent player state {recentPlayer.ActorGuid} → {recentPlayer.PlayerId}");
                        actorGuidToPlayerId[pawnActorGuid] = recentPlayer.PlayerId;
                        return recentPlayer.PlayerId;
                    }
                }
            }
        }

        LogVerbose($"    ✗ All methods failed");
        return null;
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
            LogVerbose($"\n🔔 EXTERNAL DATA on channel {channelIndex}");
            LogVerbose($"   Archive Type: {externalData.Archive.GetType().Name}");

            try
            {
                // Parse the player name data from external data
                var playerNameData = new PlayerNameData(externalData.Archive);
                LogVerbose($"   Decoded Name: {playerNameData.DecodedName ?? "NULL"}");

                // ✅ CRITICAL FIX: Pass the decoded name directly to the builder
                // The builder will match it to the player by channel
                Builder.UpdatePrivateName(channelIndex, playerNameData);

                LogVerbose($"   ✓ Updated private name for channel {channelIndex}");
            }
            catch (Exception ex)
            {
                LogVerbose($"   ✗ Error parsing external data: {ex.Message}");
            }
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

    private void PrintComplexProperty(object obj, string indent, Dictionary<string, HashSet<string>> tracker, string parentName, int depth = 0)
    {
        if (obj == null || depth > 3)
        {
            LogVerbose($"{indent}(null or max depth)");
            return;
        }

        var objType = obj.GetType();

        // Handle common types
        if (objType.IsPrimitive || objType == typeof(string) || objType == typeof(decimal))
        {
            LogVerbose($"{indent}{obj}");
            return;
        }

        // Handle enums
        if (objType.IsEnum)
        {
            LogVerbose($"{indent}{obj} ({(int) obj})");
            return;
        }

        var properties = objType.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        foreach (var prop in properties)
        {
            try
            {
                var value = prop.GetValue(obj);
                var fullName = $"{parentName}.{prop.Name}";

                if (!tracker.ContainsKey(fullName))
                {
                    tracker[fullName] = new HashSet<string>();
                }

                if (value == null)
                {
                    LogVerbose($"{indent}{prop.Name}: NULL");
                    tracker[fullName].Add("NULL");
                }
                else if (prop.PropertyType.IsPrimitive || prop.PropertyType == typeof(string))
                {
                    LogVerbose($"{indent}{prop.Name}: {value}");
                    tracker[fullName].Add(value.ToString());
                }
                else if (prop.PropertyType.IsEnum)
                {
                    LogVerbose($"{indent}{prop.Name}: {value} ({(int) value})");
                    tracker[fullName].Add($"{value}({(int) value})");
                }
                else
                {
                    LogVerbose($"{indent}{prop.Name} ({prop.PropertyType.Name}):");
                    PrintComplexProperty(value, indent + "  ", tracker, fullName, depth + 1);
                }
            }
            catch (Exception ex)
            {
                LogVerbose($"{indent}{prop.Name}: <ERROR: {ex.Message}>");
            }
        }
    }

    private void PrintEliminationPropertySummary()
    {
        LogVerbose("\n" + new string('=', 80));
        LogVerbose("ELIMINATION PROPERTY SUMMARY");
        LogVerbose(new string('=', 80));
        LogVerbose($"Total elimination events parsed: {eliminationSamples.Count}");
        LogVerbose($"Unique properties found: {eliminationPropertiesFound.Count}");
        LogVerbose("");

        foreach (var prop in eliminationPropertiesFound.OrderBy(x => x.Key))
        {
            LogVerbose($"\n{prop.Key}:");
            LogVerbose($"  Unique values: {prop.Value.Count}");

            // Show samples
            var samples = prop.Value.Take(10).ToList();
            if (samples.Any())
            {
                LogVerbose($"  Sample values:");
                foreach (var sample in samples)
                {
                    LogVerbose($"    - {sample}");
                }
            }

            // Check if always null
            if (prop.Value.Count == 1 && prop.Value.Contains("NULL"))
            {
                LogVerbose($"  ⚠️ ALWAYS NULL across all {eliminationSamples.Count} events");
            }
        }

        // Property availability matrix
        LogVerbose("\n" + new string('-', 80));
        LogVerbose("PROPERTY AVAILABILITY:");
        LogVerbose(new string('-', 80));

        foreach (var prop in eliminationPropertiesFound.OrderBy(x => x.Key))
        {
            var nullCount = prop.Value.Contains("NULL") ? 1 : 0;
            var nonNullCount = prop.Value.Count - nullCount;
            var availability = (double) nonNullCount / eliminationSamples.Count * 100;

            var status = availability >= 90 ? "✅" : availability >= 50 ? "⚠️" : "❌";
            LogVerbose($"{status} {prop.Key,-40} {availability:F1}% non-null");
        }

        LogVerbose("\n" + new string('=', 80) + "\n");
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
                LogVerbose($"\n📊 CLIENT STAT: {clientStat.StatName} = {clientStat.StatValue}");

                // Highlight harvest-related stats
                var statNameLower = clientStat.StatName.ToLower();
                if (statNameLower.Contains("wood") || statNameLower.Contains("stone") ||
                    statNameLower.Contains("metal") || statNameLower.Contains("material") ||
                    statNameLower.Contains("harvest") || statNameLower.Contains("gather") ||
                    statNameLower.Contains("resource") || statNameLower.Contains("farm"))
                {
                    LogVerbose($"  🎯 HARVEST-RELATED STAT!");

                    // Try to identify player
                    var playerId1 = Builder.GetPlayerIdFromChannel(channelIndex);
                    if (string.IsNullOrEmpty(playerId1))
                    {
                        var actorGuid1 = channelToActorGuid.GetValueOrDefault(channelIndex);
                        playerId1 = GetPlayerIdFromActor(actorGuid1);
                    }

                    LogVerbose($"  Player: {playerId1}");
                    LogVerbose($"  Channel: {channelIndex}");
                }

                ProcessClientStat(clientStat, channelIndex);
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

    private Dictionary<uint, string> _ownerActorGuidToPlayerId = new();
    private Dictionary<uint, uint> _channelToOwner = new();
    private Dictionary<uint, string> _ownerGuidToPlayerId = new();

    private string GetMaterialFromActorName(string baseName)
    {
        // Harvestable materials
        if (baseName.Contains("Rock") || baseName.Contains("Boulder") || baseName.Contains("FlintRaw"))
            return "Stone";
        if (baseName.Contains("Tree") || baseName.Contains("Fence") || baseName.Contains("Wooden") || baseName.Contains("WoodenFence") || baseName.Contains("WoodCrate") || baseName.Contains("Container_Wood") || baseName.Contains("Crate"))
            return "Wood";
        if (baseName.Contains("MetalShelving") || baseName.Contains("Industrial_Metal") || baseName.Contains("CarpetStain_Dresser"))
            return "Metal";

        // Non-harvestable (furniture, buildings, etc)
        if (baseName.Contains("Table") || baseName.Contains("Chair") || baseName.Contains("Bed") || baseName.Contains("Desk") || baseName.Contains("Window") || baseName.Contains("Door") || baseName.Contains("Wall") || baseName.Contains("Floor") || baseName.Contains("Tent") || baseName.Contains("Umbrella") || baseName.Contains("Roof") || baseName.Contains("Counter") || baseName.Contains("Shelving") || baseName.Contains("Fridge") || baseName.Contains("Cabinet") || baseName.Contains("Blockout"))
            return "Furniture";

        return "Unknown";
    }

    private Dictionary<uint, string> channelToBlueprintPath = new();
    private Dictionary<uint, int?> lastHitCount = new();


    private Dictionary<string, string> persistentIdMappingHistory = new();
    private Dictionary<uint, uint> _broadcastChannelToPlayerChannel = new();

    private HashSet<uint> harvestChannels = new();
    private HashSet<uint> broadcastChannels = new();

    protected override void OnExportRead(uint channelIndex, INetFieldExportGroup? exportGroup)
    {


        // ✅ Capture WorldPlayerId→EpicID mapping from FortPlayerState (ALL updates)
        if (exportGroup is FortPlayerState playerState)
        {
            // ✅ Map actor GUID to player ID for damage tracking
            if (Channels[channelIndex]?.Actor?.ActorNetGUID != null)
            {
                var actorGuid = Channels[channelIndex].Actor.ActorNetGUID.Value;
                var playerId = Builder.GetPlayerIdFromChannel(channelIndex);

                if (!string.IsNullOrEmpty(playerId) && actorGuid != 0)
                {
                    actorGuidToPlayerId[actorGuid] = playerId;
                }
            }

            if (playerState.WorldPlayerId.HasValue && !string.IsNullOrEmpty(playerState.UniqueID))
            {
                short worldPlayerId = playerState.WorldPlayerId.Value;
                string epicId = playerState.UniqueID;
                float currentTime = (float) Builder.GetCurrentTimeDouble();

                if (epicId.Contains("["))
                    epicId = epicId.Split('[')[0].Trim();

                // ✅ Always update the mapping, even if we've seen this WorldPlayerId before
                Builder.persistentIdToPlayerId[worldPlayerId] = epicId;

                // Only log on NEW mappings to reduce spam
                if (!Builder.persistentIdToPlayerId.ContainsKey(worldPlayerId))
                {
                    LogVerbose($"✅ Mapped WorldPlayerId {worldPlayerId} → {epicId} at {currentTime}s");
                }
            }
        }

        if (exportGroup is PlayerPawnCache pawnCache)
        {
            // Check for gameplay cues
            if (pawnCache.InvokeGameplayCueAdded != null)
            {
                var cue = pawnCache.InvokeGameplayCueAdded;

                LogVerbose($"\n🎮 GAMEPLAY CUE ADDED");
                LogVerbose($"  Tag: {cue.GameplayCueTag?.TagName}");

                if (cue.Parameters != null)
                {
                    LogVerbose($"  Location: {cue.Parameters.Location}");
                    LogVerbose($"  Instigator: {cue.Parameters.Instigator}");
                    LogVerbose($"  EffectCauser: {cue.Parameters.EffectCauser}");
                    LogVerbose($"  SourceObject: {cue.Parameters.SourceObject}");
                }

                // Always print cue summary
                var tagName = cue.GameplayCueTag?.TagName?.ToLower() ?? "";
                var isHarvestRelated =
                    tagName.Contains("harvest") ||
                    tagName.Contains("pickaxe") ||
                    tagName.Contains("gather") ||
                    tagName.Contains("resource");

                if (isHarvestRelated)
                {
                    LogVerbose("  🪓 HARVEST-RELATED CUE DETECTED");
                }

                // Optional: print who triggered it if known
                if (cue.Parameters?.Instigator > 0)
                {
                    var playerId = GetPlayerIdFromActor(cue.Parameters.Instigator);
                    if (!string.IsNullOrEmpty(playerId))
                        LogVerbose($"  ▶ Triggered by Player: {playerId}");
                }

                LogVerbose(new string('-', 60));
            }
        }

        var actualType = exportGroup.GetType();
        var fullName = actualType.FullName;
        var assemblyName = actualType.Assembly.GetName().Name;

        switch (exportGroup)
        {
            case GameState state:
                    


                Builder.UpdateGameState(state);
                break;

            case PlaylistInfo playlist:
                Builder.UpdatePlaylistInfo(playlist);
                break;



            case PlayerPawn pawn:
                LogVerbose($"\n=== PLAYER PAWN UPDATE ===");
                LogVerbose($"PawnUniqueID: {pawn.PawnUniqueID}");
                Builder.UpdatePlayerPawn(channelIndex, pawn);
                ProcessPlayerPawn(pawn, channelIndex);
                PrintObjectProperties(pawn, "PlayerPawn", 0);
                break;

            case PlayerBuild playerBuild:
                LogVerbose($"[DEBUG] PlayerBuild detected");
                LogVerbose($"[DEBUG] Owner: {playerBuild.Owner}");
                LogVerbose($"[DEBUG] Channel: {channelIndex}");
                break;



            case FortPlayerState state:
                var channel = Channels[channelIndex];
                var playerStateActorGuid = channel?.Actor?.ActorNetGUID?.Value;

                Builder.UpdatePlayerState(channelIndex, state);
                ProcessPlayerState(state, channelIndex);

                // ✅ NEW: Map the player state actor GUID
                var playerIdFromState = Builder.GetPlayerIdFromAnyChannel(channelIndex);
                if (!string.IsNullOrEmpty(playerIdFromState) && playerStateActorGuid.HasValue && playerStateActorGuid.Value != 0)
                {
                    actorGuidToPlayerId[playerStateActorGuid.Value] = playerIdFromState;
                    Console.WriteLine($"📊 Mapped PlayerState Actor {playerStateActorGuid.Value} → Player {playerIdFromState}");
                }
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
                    LogVerbose($"\n=== MATERIAL PICKUP DEBUG ===");
                    PrintObjectProperties(pickup, "FortPickup", 0);
                    LogVerbose($"=== END DEBUG ===\n");
                }
                break;

            case HealthSet healthSet:
                AnalyzeForStats(healthSet, "HealthSet", channelIndex);
                break;

            // Wood walls & windows
            case WoodWall:
            case WoodArchwayWall:
            case WoodBraceWall:
            case WoodDoorSideWall:
            case WoodDoorWall:
            case WoodWindowSideWall:
            case WoodWindowCenterWall:
                ProcessBuild(channelIndex, (BaseBuild) exportGroup, "Wall", "Wood");
                break;

            // Wood half & quarter walls
            case WoodHalfWall:
            case WoodQuarterWall:
                ProcessBuild(channelIndex, (BaseBuild) exportGroup, "Wall", "Wood");
                break;

            // Wood roofs
            case WoodRoof:
            case WoodRoofI:
            case WoodRoofOctagonal:
            case WoodRoofSlope:
            case WoodRoofDome:
            case WoodRoofWall:
            case WoodArchwayLargeSupport:
            case WoodBalconyOuter:
                ProcessBuild(channelIndex, (BaseBuild) exportGroup, "Roof", "Wood");
                break;

            // Wood stairs
            case WoodStair:
            case WoodStairF:
            case WoodStairT:
                ProcessBuild(channelIndex, (BaseBuild) exportGroup, "Stair", "Wood");
                break;

            // Wood floors
            case WoodFloor:
            case WoodBalconyIFloor:
            case WoodBalconySFloor:
                ProcessBuild(channelIndex, (BaseBuild) exportGroup, "Floor", "Wood");
                break;

            // Stone walls & windows
            case StoneWall:
            case StoneArchwayWall:
            case StoneBraceWall:
            case StoneDoorSideWall:
            case StoneDoorWall:
            case StoneWindowSideWall:
            case StoneWindowsSideWall:
            case StoneWindowsCenterWall:
                ProcessBuild(channelIndex, (BaseBuild) exportGroup, "Wall", "Stone");
                break;

            // Stone half & quarter walls
            case StoneHalfWall:
            case StoneQuarterWall:
                ProcessBuild(channelIndex, (BaseBuild) exportGroup, "Wall", "Stone");
                break;

            // Stone roofs
            case StoneRoof:
            case StoneRoofDome:
            case StoneRoofOctagonal:
            case StoneRoofSlope:
            case StoneRoofWall:
            case StoneRoofInterior:
            case StoneBalconyInterior:
            case StoneBalconyOuter:
            case StoneBalconySmall:
            case StoneBalconyDome:
            case StoneArchwayLargeSupport:
                ProcessBuild(channelIndex, (BaseBuild) exportGroup, "Roof", "Stone");
                break;

            // Stone stairs
            case StoneStair:
            case StoneStairF:
            case StoneStairT:
                ProcessBuild(channelIndex, (BaseBuild) exportGroup, "Stair", "Stone");
                break;

            // Stone floors
            case StoneFloor:
                ProcessBuild(channelIndex, (BaseBuild) exportGroup, "Floor", "Stone");
                break;

            // Metal walls & windows
            case MetalWall:
            case MetalArchwayWall:
            case MetalBraceWall:
            case MetalDoorSideWall:
            case MetalDoorWall:
            case MetalWindowSideWall:
            case MetalWindowCenterWall:
            case MetalHalfWall:
            case MetalQuarterWall:
                ProcessBuild(channelIndex, (BaseBuild) exportGroup, "Wall", "Metal");
                break;

            // Metal roofs & archways
            case MetalRoof:
            case MetalRoofOctagonal:
            case MetalRoofInterior:
            case MetalRoofSlope:
            case MetalRoofDome:
            case MetalRoofWall:
            case MetalArchway:
            case MetalBalconyInterior:
            case MetalBalconySmall:
            case MetalBalconyOuter:
            case MetalBalconyDome:
            case MetalArchwayLargeSupport:
                ProcessBuild(channelIndex, (BaseBuild) exportGroup, "Roof", "Metal");
                break;

            // Metal stairs
            case MetalStair:
            case MetalStairF:
                ProcessBuild(channelIndex, (BaseBuild) exportGroup, "Stair", "Metal");
                break;

            // Metal floors
            case MetalFloor:
                ProcessBuild(channelIndex, (BaseBuild) exportGroup, "Floor", "Metal");
                break;

            case FortBroadcastRemoteClientInfo remoteClientInfo:
                harvestChannels.Add(channelIndex);

                // ✅ DEBUG: Log all properties via introspection
                var props = remoteClientInfo.GetType().GetProperties();
                var propStrings = new List<string>();
                foreach (var prop in props)
                {
                    try
                    {
                        var value = prop.GetValue(remoteClientInfo);
                        propStrings.Add($"{prop.Name}={value}");
                    }
                    catch { }
                }
                Console.WriteLine($"[FBRCI] Channel {channelIndex}: {string.Join(", ", propStrings)}");

                // ✅ CAPTURE: Owner GUID on first appearance and find out what it actually is
                if (remoteClientInfo.Owner.HasValue && remoteClientInfo.Owner.Value != 0)
                {
                    var ownerGuid = remoteClientInfo.Owner.Value;

                    // Log what archetype/path this Owner GUID actually points to
                    if (_netGuidCache.TryGetPathName(ownerGuid, out var ownerPath))
                    {
                        Console.WriteLine($"🔍 [OWNER_GUID_DEBUG] Channel {channelIndex}: Owner={ownerGuid} points to {ownerPath}");
                    }
                    else
                    {
                        Console.WriteLine($"🔍 [OWNER_GUID_DEBUG] Channel {channelIndex}: Owner={ownerGuid} → <path unknown>");
                    }

                    // Store it regardless
                    broadcastChannelToOwnerGuid[channelIndex] = ownerGuid;
                }

                break;


            case FortSafeZoneIndicatorFuture safeZoneFuture:
                Console.WriteLine($"\n[SAFEZONE_FUTURE_DEBUG] FortSafeZoneIndicatorFuture properties:");
                var props_ = safeZoneFuture.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                foreach (var prop in props_)
                {
                    try
                    {
                        var value = prop.GetValue(safeZoneFuture);
                        if (value != null)
                        {
                            Console.WriteLine($"  {prop.Name}: {value}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  {prop.Name}: [Error: {ex.Message}]");
                    }
                }

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

        // ✅ ADD THIS AFTER THE SWITCH:
        // Register the export group by its channel's actor GUID
        if (exportGroup != null && Channels[channelIndex]?.Actor?.ActorNetGUID != null)
        {
            var actorGuid = Channels[channelIndex].Actor.ActorNetGUID;
            Builder.RegisterExportGroup(actorGuid, exportGroup);
            Console.WriteLine($"[REGISTRY] Registered {exportGroup.GetType().Name} for actor GUID {actorGuid.Value}");
        }
    }

    private void PostProcessHarvestHits()
    {
        Console.WriteLine("\n" + new string('=', 80));
        Console.WriteLine("🎯 HARVEST TRACKING SUMMARY");
        Console.WriteLine(new string('=', 80));

        Console.WriteLine($"\n📡 BROADCAST CHANNELS OPENED: {broadcastChannels.Count}");
        foreach (var ch in broadcastChannels.OrderBy(x => x).Take(20))
        {
            var playerId = channelToPlayerId.TryGetValue(ch, out var pid) ? pid : "UNRESOLVED";
            Console.WriteLine($"  Channel {ch} → {playerId}");
        }
        if (broadcastChannels.Count > 20)
            Console.WriteLine($"  ... and {broadcastChannels.Count - 20} more");

        Console.WriteLine($"\n🌲 CHANNELS WITH HARVESTS: {harvestChannels.Count}");
        foreach (var ch in harvestChannels.OrderBy(x => x).Take(20))
        {
            var playerId = channelToPlayerId.TryGetValue(ch, out var pid) ? pid : "UNRESOLVED";
            Console.WriteLine($"  Channel {ch} → {playerId}");
        }
        if (harvestChannels.Count > 20)
            Console.WriteLine($"  ... and {harvestChannels.Count - 20} more");

        var resolved = harvestChannels.Count(ch => channelToPlayerId.ContainsKey(ch) && !string.IsNullOrEmpty(channelToPlayerId[ch]));
        Console.WriteLine($"\n✅ Resolved: {resolved}/{harvestChannels.Count}");
        Console.WriteLine($"❌ Unresolved: {harvestChannels.Count - resolved}/{harvestChannels.Count}");

        Console.WriteLine(new string('=', 80) + "\n");
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

    private int deduplicationHits = 0;
    private Dictionary<short, int> _buildCountByOwnerPersistentId = new();
    private void ProcessBuild(uint channelIndex, BaseBuild build, string buildTypeName, string material)
    {
        if (!(build.bPlayerPlaced == true || build.bIsInitiallyBuilding == true))
        {
            return;
        }

        if (!build.TeamIndex.HasValue)
        {
            return;
        }

        var currentTimeValue = Builder.GetCurrentTimeDouble();
        var currentTime = (float) currentTimeValue;

        if (build.EditingPlayer?.Value > 0)
        {
            return;
        }

        if (build.bDestroyed == true)
        {
            return;
        }

        if (build.bIsInitiallyBuilding != true)
        {
            return;
        }

        short? expectedHealth = GetExpectedInitialHealth(material, buildTypeName);
        if (expectedHealth.HasValue && build.Health != expectedHealth.Value)
        {
            return;
        }

        long timeMs = (long) (currentTimeValue * 1000);
        string buildKey = $"{channelIndex}_{timeMs}";

        if (processedBuilds.Contains(buildKey))
        {
            return;
        }

        // ✅ Use OwnerPersistentID to get Epic ID and record build
        if (build.OwnerPersistentID != null)
        {
            short ownerPersistentId = build.OwnerPersistentID.Value.Value;

            if (Builder.persistentIdToPlayerId.TryGetValue(ownerPersistentId, out var epicId))
            {
                BuildTracker.RecordBuild(
                    epicId.ToLower(),
                    material,
                    buildTypeName,
                    build.bPlayerPlaced ?? true,
                    build.bDestroyed ?? false,
                    build.TeamIndex ?? 0,
                    build.Health ?? 100,
                    build.EditingPlayer?.Value,
                    currentTime,
                    ownerPersistentId
                );

                LogVerbose($"[BUILD_RECORDED] {epicId}: {buildTypeName} {material}");
            }
            else
            {
                LogVerbose($"[BUILD_SKIP] OwnerPersistentID {ownerPersistentId} - No Epic ID mapping found");
            }
        }
        else
        {
            LogVerbose($"[BUILD_SKIP] Channel {channelIndex} - No OwnerPersistentID");
        }

        processedBuilds.Add(buildKey);
    }
    private void MapPlayerStatesForBuildTracking()
    {
        LogVerbose("\n[BUILD_MAPPING] Mapping OwnerPersistentID to Epic IDs...");

        // Use the mapping from Builder which captures ALL updates
        foreach (var kvp in Builder.persistentIdToPlayerId)  // Make this public
        {
            _ownerPersistentIdToEpicId[kvp.Key] = kvp.Value;
        }


        LogVerbose($"[BUILD_MAPPING] From replay data: {_ownerPersistentIdToEpicId.Count} players");
        LogVerbose($"[BUILD_MAPPING] Builds have {_buildCountByOwnerPersistentId.Count} unique owners");

        // Map remaining owners as UNMAPPED
        foreach (var ownerPersistentId in _buildCountByOwnerPersistentId.Keys)
        {
            if (!_ownerPersistentIdToEpicId.ContainsKey(ownerPersistentId))
            {
                _ownerPersistentIdToEpicId[ownerPersistentId] = $"UNMAPPED_{ownerPersistentId}";
            }
        }

        LogVerbose($"[BUILD_MAPPING] Final mapped: {_ownerPersistentIdToEpicId.Count} owners\n");
    }


    public void LogBuildCountsByOwner()
    {
        LogVerbose("\n" + new string('=', 80));
        LogVerbose("📊 BUILD COUNTS BY OWNER PERSISTENT ID");
        LogVerbose(new string('=', 80));

        foreach (var kvp in _buildCountByOwnerPersistentId.OrderByDescending(x => x.Value))
        {
            LogVerbose($"  OwnerPersistentID {kvp.Key}: {kvp.Value} builds");
        }

        LogVerbose(new string('=', 80) + "\n");
    }
    private short? GetExpectedInitialHealth(string material, string buildType)
    {
        return (material.ToLower(), buildType.ToLower()) switch
        {
            ("wood", "wall") => 90,
            ("wood", "floor") => 84,
            ("wood", "roof") => 84,
            ("wood", "stair") => 84,
            ("stone", "wall") => 99,
            ("stone", "floor") => 93,
            ("stone", "roof") => 93,
            ("stone", "stair") => 93,
            ("metal", "wall") => 110,
            ("metal", "floor") => 101,
            ("metal", "roof") => 101,
            ("metal", "stair") => 101,
            _ => null
        };
    }


    private string GetEpicIdFromPersistentId(short pawnUniqueId)
    {
        LogVerbose($"[DEBUG] Looking up Epic ID for pawnUniqueId: {pawnUniqueId}");

        if (pawnUniqueIdToPlayerId.TryGetValue(pawnUniqueId, out var playerId))
        {
            LogVerbose($"[DEBUG] Found playerId: {playerId}");

            if (playerIdToEpicId.TryGetValue(playerId, out var epicId))
            {
                LogVerbose($"[DEBUG] Found epicId: {epicId}");
                return epicId;
            }
            else
            {
                LogVerbose($"[DEBUG] No epicId found for playerId: {playerId}");
            }
        }
        else
        {
            LogVerbose($"[DEBUG] No playerId found for pawnUniqueId: {pawnUniqueId}");
        }

        LogVerbose($"[DEBUG] Returning null for pawnUniqueId: {pawnUniqueId}");
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

        var playerIdFromBuilder = Builder.GetPlayerIdFromAnyChannel(channelIndex);

        if (string.IsNullOrEmpty(playerIdFromBuilder))
        {
            LogVerbose($"⚠️ Could not get player ID from builder for channel {channelIndex}");
            return; // Exit early if we can't identify the player
        }

        // ✅ LET THE BUILDER HANDLE THE PLAYER FIRST
        // The builder creates the player and determines the correct ID format
        Builder.UpdatePlayerState(channelIndex, state);

        if (state.PlayerTeam != null)
        {
            //LogVerbose($"PlayerTeam: {state.PlayerTeam}");
            var props = state.PlayerTeam.GetType().GetProperties();
            foreach (var prop in props)
            {
                try
                {
                    var value = prop.GetValue(state.PlayerTeam);
                    LogVerbose($"  {prop.Name} = {value}");
                }
                catch { }
            }
        }

        LogVerbose($"\n🔍 PLAYER STATE DEBUG:");
        LogVerbose($"   Channel: {channelIndex}");
        LogVerbose($"   Actor GUID: {actorGuid}");
        LogVerbose($"   Player ID from Builder: {playerIdFromBuilder}");

        // ✅ LOG ATHENAKILLS UPDATE
        if (state.AthenaKills.HasValue)
        {
            //LogVerbose($"🎯 AthenaKills UPDATE: {playerIdFromBuilder} = {state.AthenaKills.Value}");
            LogVerbose($"   AthenaKills: {state.AthenaKills.Value}");

            // Store it in playerRealtimeStats
            if (!string.IsNullOrEmpty(playerIdFromBuilder))
            {
                if (!playerRealtimeStats.ContainsKey(playerIdFromBuilder))
                    playerRealtimeStats[playerIdFromBuilder] = new Dictionary<string, object>();

                playerRealtimeStats[playerIdFromBuilder]["AthenaKills"] = state.AthenaKills.Value;
            }
        }
        else
        {
            //LogVerbose($"🎯 AthenaKills UPDATE: {playerIdFromBuilder} = NULL");
            LogVerbose($"   AthenaKills: NULL");
        }

        // ✅ MAP ACTOR TO THE BUILDER'S PLAYER ID
        if (actorGuid.HasValue && !string.IsNullOrEmpty(playerIdFromBuilder))
        {
            actorGuidToPlayerId[actorGuid.Value] = playerIdFromBuilder;
            LogVerbose($"   ✅ MAPPED: Actor {actorGuid.Value} → Player {playerIdFromBuilder}");

            DamageTracker.MapActorToPlayer(actorGuid.Value, playerIdFromBuilder);

            if (!playerInfoByPlayerId.ContainsKey(playerIdFromBuilder))
            {
                var playerInfo = new PlayerInfo(playerIdFromBuilder, channelIndex, actorGuid.Value, playerIdFromBuilder, actorGuid.Value);

                if (state.TeamIndex.HasValue)
                {
                    playerInfo.TeamIndex = (byte) state.TeamIndex.Value;
                }

                playerInfoByPlayerId[playerIdFromBuilder] = playerInfo;
            }
        }

        // ✅ NEW: Also map the Pawn GUID if this is a pawn channel
        // The broadcast channels have Owner pointing to the pawn, so we need this mapping
        if (actorGuid.HasValue && actorGuid.Value != 0 && !string.IsNullOrEmpty(playerIdFromBuilder))
        {
            // Check if this is a pawn actor by looking at the channel's actor
            if (channel?.Actor != null)
            {
                var archetypeGuid = channel.Actor.Archetype?.Value;
                if (archetypeGuid.HasValue && _netGuidCache.TryGetPathName(archetypeGuid.Value, out var archetypePath))
                {
                    if (archetypePath.Contains("FortPawnAthena") || archetypePath.Contains("PlayerPawn"))
                    {
                        // This is a pawn - map it for broadcast channel lookup
                        actorGuidToPlayerId[actorGuid.Value] = playerIdFromBuilder;
                        Console.WriteLine($"🎮 Mapped Pawn {actorGuid.Value} → Player {playerIdFromBuilder}");
                    }
                }
            }
        }

        // Rest of your material tracking code...
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

                    if (!string.IsNullOrEmpty(playerIdFromBuilder))
                    {
                        if (!playerRealtimeStats.ContainsKey(playerIdFromBuilder))
                            playerRealtimeStats[playerIdFromBuilder] = new Dictionary<string, object>();

                        playerRealtimeStats[playerIdFromBuilder][propName] = value ?? 0;
                    }
                }
                catch (Exception ex)
                {
                    LogVerbose($"  {propName}: <error reading: {ex.Message}>");
                }
            }
        }

        AnalyzeForStats(state, "FortPlayerState", channelIndex, playerIdFromBuilder);
    }

    private Dictionary<short, string> pawnUniqueIdToEpicId = new();

    private Dictionary<short, string> pawnUniqueIdToPlayerId = new();

    private void ProcessPlayerPawn(PlayerPawn pawn, uint channelIndex)
    {
        var pawnChannel = Channels[channelIndex];
        var pawnActorGuid = pawnChannel?.Actor?.ActorNetGUID?.Value;
        var playerStateActorGuid = pawn.PlayerState;

        Console.WriteLine($"[PAWN_DEBUG] Channel: {channelIndex}, PawnUniqueID: {pawn.PawnUniqueID}, Actor: {pawnActorGuid}");

        Console.WriteLine($"=== PLAYER PAWN on Channel {channelIndex} ===");
        Console.WriteLine($"Pawn Actor GUID: {pawnActorGuid}");
        Console.WriteLine($"PlayerState GUID: {playerStateActorGuid}");

        string? pawnPlayerId = null;

        if (pawnActorGuid.HasValue)
        {
            LogVerbose("Looking up pawn actor in builder...");
            LogVerbose($"Pawn Actor GUID: {pawnActorGuid.Value}");
            var pawnPlayerId1 = Builder.ResolveActorIdToPlayerId(pawnActorGuid.Value);

            if (pawnPlayerId1 != null)
            {
                LogVerbose($"Builder resolved pawn actor to player ID: {pawnPlayerId1}");
            }
            else
            {
                LogVerbose($"Builder could not resolve pawn actor GUID to player ID");
            }
        }
        else
        {
            LogVerbose($"Pawn Actor GUID: NULL");
        }

        // Try to resolve player ID
        if (pawnActorGuid.HasValue && actorGuidToPlayerId.ContainsKey(pawnActorGuid.Value))
        {
            pawnPlayerId = actorGuidToPlayerId[pawnActorGuid.Value];
            LogVerbose($"✓ PawnUniqueID:{pawn.PawnUniqueID} Found player via pawn actor GUID: {pawnPlayerId}");
        }

        else if (playerStateActorGuid.HasValue && actorGuidToPlayerId.ContainsKey(playerStateActorGuid.Value))
        {
            pawnPlayerId = actorGuidToPlayerId[playerStateActorGuid.Value];
            LogVerbose($"✓ PawnUniqueID:{pawn.PawnUniqueID} Found player via PlayerState GUID: {pawnPlayerId}");

            // CRITICAL: Map the pawn to this player
            if (pawnActorGuid.HasValue)
            {
                actorGuidToPlayerId[pawnActorGuid.Value] = pawnPlayerId;

                if (playerInfoByPlayerId.TryGetValue(pawnPlayerId, out var playerInfo))
                {
                    pawnToPlayerState[pawnActorGuid.Value] = playerInfo;
                    LogVerbose($"✅ MAPPED PAWN: {pawnActorGuid.Value} → {pawnPlayerId}");
                }

                DamageTracker.MapActorToPlayer(pawnActorGuid.Value, pawnPlayerId);

                // ✅ ADD THIS: Map channel to player
                channelToPlayerId[channelIndex] = pawnPlayerId;

                LogVerbose($"✓ LINKED PAWN: Actor {pawnActorGuid.Value} -> Player {pawnPlayerId}");
            }
        }
        else
        {
            LogVerbose($"⚠️ Could not identify player for pawn");
        }

        // ✅ Track activity AFTER we've populated channelToPlayerId
        if (!string.IsNullOrEmpty(pawnPlayerId))
        {
            channelToPlayerId[channelIndex] = pawnPlayerId;
            LogVerbose($"✅ MAPPED PAWN CHANNEL: {channelIndex} → Player {pawnPlayerId}");


            var currentTime = (float) Builder.GetCurrentTimeDouble();
            lastChannelActivityTime[channelIndex] = currentTime;
            mostRecentActiveChannel = channelIndex;
            LogVerbose($"🎯 Set mostRecentActiveChannel = {channelIndex} for player {pawnPlayerId}");
        }

        LogVerbose($"Final Player: {pawnPlayerId ?? "UNKNOWN"}");

        // Check for PawnUniqueID and map it
        var pawnType = pawn.GetType();
        var pawnIdProp = pawnType.GetProperty("PawnUniqueID");

        if (pawnIdProp != null)
        {
            try
            {
                var pawnUniqueId = pawnIdProp.GetValue(pawn);
                LogVerbose($"PawnUniqueID: {pawnUniqueId} (Type: {pawnIdProp.PropertyType.Name})");

                if (pawnUniqueId != null)
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
                        if (!pawnUniqueIdToPlayerId.ContainsKey(persistentId))
                        {
                            string playerId = null;

                            // First, try pawnPlayerId (if we resolved it in this call)
                            if (!string.IsNullOrEmpty(pawnPlayerId))
                            {
                                playerId = pawnPlayerId;
                                LogVerbose($"✓ Got Player from pawnPlayerId: {playerId}");
                            }

                            // Second, try channel mapping
                            if (string.IsNullOrEmpty(playerId) && channelToPlayerId.TryGetValue(channelIndex, out var channelPlayerId))
                            {
                                playerId = channelPlayerId;
                                LogVerbose($"✓ Got Player from channel {channelIndex}: {playerId}");
                            }

                            if (!string.IsNullOrEmpty(playerId))
                            {
                                pawnUniqueIdToPlayerId[persistentId] = playerId;
                                LogVerbose($"✓ Mapped PawnUniqueID {persistentId} → {playerId}");
                            }
                            else
                            {
                                LogVerbose($"⚠ PawnUniqueID {persistentId} = UNRESOLVED (no player found)");
                            }
                        }
                        else
                        {
                            LogVerbose($"⚠ PawnUniqueID {persistentId} already mapped");
                        }
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

        // Track player position for build ownership
        if (!string.IsNullOrEmpty(pawnPlayerId))
        {
            Vector3 position = default;
            bool hasPosition = false;

            if (pawn.Location != null)
            {
                position = new Vector3((float) pawn.Location.X, (float) pawn.Location.Y, (float) pawn.Location.Z);
                hasPosition = true;
                LogVerbose($"✓ Got position from pawn.Location: {position}");
            }
            else if (pawn.ReplicatedMovement.HasValue && pawn.ReplicatedMovement.Value.Location != null)
            {
                var loc = pawn.ReplicatedMovement.Value.Location;
                position = new Vector3((float) loc.X, (float) loc.Y, (float) loc.Z);
                hasPosition = true;
                LogVerbose($"✓ Got position from ReplicatedMovement: {position}");
            }
            else
            {
                LogVerbose($"⚠️ No position data available");
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
        Console.WriteLine($"\n[RAW_DAMAGE_RPC] Ch{channelIndex} received damage event");
        Console.WriteLine($"  Magnitude: {damageCues.Magnitude} (HasValue: {damageCues.Magnitude.HasValue})");
        Console.WriteLine($"  HitActor: {damageCues.HitActor} (HasValue: {damageCues.HitActor.HasValue})");
        Console.WriteLine($"  bIsCritical: {damageCues.bIsCritical}");
        Console.WriteLine($"  bIsFatal: {damageCues.bIsFatal}");
        Console.WriteLine($"  SourceWeapon: {damageCues.SourceWeapon}");

        // ✅ FILTER 1: Only process if there's actual damage
        if (!damageCues.Magnitude.HasValue || damageCues.Magnitude.Value <= 0)
        {
            Console.WriteLine($"  [FILTER_1_FAIL] Magnitude not valid: {damageCues.Magnitude}");
            return;
        }
        Console.WriteLine($"  [FILTER_1_PASS] Magnitude = {damageCues.Magnitude.Value}");

        var attackerChannel = Channels[channelIndex];
        string? attackerPlayerId = null;
        if (attackerChannel?.Actor?.ActorNetGUID != null)
        {
            var attackerActorGuid = attackerChannel.Actor.ActorNetGUID.Value;
            Console.WriteLine($"  [ATTACKER_LOOKUP] Ch{channelIndex} ActorGuid: {attackerActorGuid}");
            actorGuidToPlayerId.TryGetValue(attackerActorGuid, out attackerPlayerId);
            Console.WriteLine($"    → PlayerId: {attackerPlayerId ?? "NOT_FOUND"}");
        }
        else
        {
            Console.WriteLine($"  [ATTACKER_LOOKUP] Ch{channelIndex} has no ActorNetGUID");
        }

        string? victimPlayerId = null;
        if (damageCues.HitActor.HasValue)
        {
            var hitActorValue = damageCues.HitActor.Value;
            Console.WriteLine($"  [VICTIM_LOOKUP] HitActor: {hitActorValue}");
            Console.WriteLine($"    Map size: {actorGuidToPlayerId.Count}");
            Console.WriteLine($"    Sample keys: {string.Join(", ", actorGuidToPlayerId.Keys.Take(10))}");

            bool found = actorGuidToPlayerId.TryGetValue(hitActorValue, out victimPlayerId);
            Console.WriteLine($"    → PlayerId: {victimPlayerId ?? "NOT_FOUND"} (Found: {found})");
        }
        else
        {
            Console.WriteLine($"  [VICTIM_LOOKUP] HitActor has no value");
        }

        // ✅ FILTER 2: Must have both attacker and victim
        if (string.IsNullOrEmpty(attackerPlayerId) || string.IsNullOrEmpty(victimPlayerId))
        {
            Console.WriteLine($"  [FILTER_2_FAIL] Missing attacker={attackerPlayerId ?? "NULL"} or victim={victimPlayerId ?? "NULL"}");
            return;
        }
        Console.WriteLine($"  [FILTER_2_PASS] Both IDs found: {attackerPlayerId} → {victimPlayerId}");

        // ✅ FILTER 3: Can't damage yourself
        if (attackerPlayerId.Equals(victimPlayerId, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"  [FILTER_3_FAIL] Self damage");
            return;
        }
        Console.WriteLine($"  [FILTER_3_PASS] Different players");

        // ✅ FILTER 4: Get player data and check if they're real players
        var allPlayers = Builder.GetAllPlayers().ToList();
        Console.WriteLine($"  [FILTER_4] Total players in builder: {allPlayers.Count}");

        var attackerPlayer = allPlayers.FirstOrDefault(p =>
            p.PlayerId.Equals(attackerPlayerId, StringComparison.OrdinalIgnoreCase));
        var victimPlayer = allPlayers.FirstOrDefault(p =>
            p.PlayerId.Equals(victimPlayerId, StringComparison.OrdinalIgnoreCase));

        Console.WriteLine($"    Attacker found: {(attackerPlayer != null ? $"{attackerPlayer.PlayerName}" : "NOT_FOUND")}");
        Console.WriteLine($"    Victim found: {(victimPlayer != null ? $"{victimPlayer.PlayerName}" : "NOT_FOUND")}");

        if (attackerPlayer == null || victimPlayer == null)
        {
            Console.WriteLine($"  [FILTER_4_FAIL] Player not found in builder");
            return;
        }

        if (attackerPlayer.IsBot || victimPlayer.IsBot)
        {
            Console.WriteLine($"  [FILTER_4B_FAIL] Bot detected - Attacker.IsBot={attackerPlayer.IsBot}, Victim.IsBot={victimPlayer.IsBot}");
            return;
        }
        Console.WriteLine($"  [FILTER_4_PASS] Both are real players");

        // ✅ FILTER 5: Check for valid player names
        if (string.IsNullOrEmpty(attackerPlayer.PlayerName) || string.IsNullOrEmpty(victimPlayer.PlayerName))
        {
            Console.WriteLine($"  [FILTER_5_FAIL] Missing names - Attacker: '{attackerPlayer.PlayerName}', Victim: '{victimPlayer.PlayerName}'");
            return;
        }

        if (attackerPlayer.PlayerName.Equals(attackerPlayer.PlayerId, StringComparison.OrdinalIgnoreCase) ||
            victimPlayer.PlayerName.Equals(victimPlayer.PlayerId, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"  [FILTER_5B_FAIL] Fake player name");
            return;
        }
        Console.WriteLine($"  [FILTER_5_PASS] Both have valid names");

        // ✅ FILTER 6: Team check
        Console.WriteLine($"    Attacker Team: {attackerPlayer.TeamIndex}, Victim Team: {victimPlayer.TeamIndex}");
        if (attackerPlayer.TeamIndex == victimPlayer.TeamIndex && attackerPlayer.TeamIndex > 0)
        {
            Console.WriteLine($"  [FILTER_6_FAIL] Same team");
            return;
        }
        Console.WriteLine($"  [FILTER_6_PASS] Different teams");

        // ✅ FILTER 7: Don't count damage to knocked players
        var playerTimelines = Builder.GetPlayerStateHistory();
        bool victimIsKnockedDown = false;
        if (playerTimelines.ContainsKey(victimPlayerId))
        {
            var victimHistory = playerTimelines[victimPlayerId];
            float currentTime = (float) Builder.GetCurrentTimeDouble();

            var knockedStatus = victimHistory
                .Where(h => h.timestamp <= currentTime)
                .OrderByDescending(h => h.timestamp)
                .FirstOrDefault();

            if (knockedStatus.property == "bDBNO" && knockedStatus.value is bool bdbno && bdbno == true)
            {
                victimIsKnockedDown = true;
            }
        }

        if (victimIsKnockedDown)
        {
            Console.WriteLine($"  [FILTER_7_FAIL] Victim knocked down");
            return;
        }
        Console.WriteLine($"  [FILTER_7_PASS] Victim not knocked");

        // ✅ ALL FILTERS PASSED
        uint damageAmount = (uint) damageCues.Magnitude.Value;
        DamageTracker.RecordDamage(attackerPlayerId, victimPlayerId, damageAmount, false);

        Console.WriteLine($"  [DAMAGE_RECORDED] {attackerPlayer.PlayerName} → {victimPlayer.PlayerName} ({damageAmount} damage)");
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
        LogVerbose("\n=== ALL EXPORT TYPES SEEN ===");
        foreach (var type in allExportTypes.OrderBy(t => t))
        {
            LogVerbose($"  {type}");
        }
        LogVerbose($"Total unique types: {allExportTypes.Count}");
        LogVerbose("=============================\n");
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

    public virtual PlayerElimination ParseElimination(FArchive archive, Unreal.Core.Models.EventInfo info)
    {
        try
        {
            var elim = new PlayerElimination { Info = info };
            var version = archive.ReadInt32();

            //LogVerbose($"\n=== PARSING ELIMINATION ===");
            //LogVerbose($"Version: {version}");

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

            //LogVerbose($"Parsing eliminated player...");
            var eliminatedPosStart = archive.Position;
            ParsePlayer(archive, elim.EliminatedInfo, version);
            var eliminatedBytesRead = archive.Position - eliminatedPosStart;
            //LogVerbose($"  Eliminated: '{elim.EliminatedInfo.Id}' (Type: {elim.EliminatedInfo.PlayerType}) - Read {eliminatedBytesRead} bytes");

            //LogVerbose($"Parsing eliminator...");
            var eliminatorPosStart = archive.Position;
            ParsePlayer(archive, elim.EliminatorInfo, version);
            var eliminatorBytesRead = archive.Position - eliminatorPosStart;
            //LogVerbose($"  Eliminator: '{elim.EliminatorInfo.Id}' (Type: {elim.EliminatorInfo.PlayerType}) - Read {eliminatorBytesRead} bytes");

            elim.GunType = archive.ReadByte();
            elim.Knocked = archive.ReadUInt32AsBoolean();
            elim.Time = info.StartTime.MillisecondsToTimeStamp();

            LogVerbose($"GunType: {elim.GunType}, Knocked: {elim.Knocked}");
            LogVerbose($"========================\n");

            return elim;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error while parsing PlayerElimination at timestamp {}", info?.StartTime);
            throw new PlayerEliminationException($"Error while parsing PlayerElimination at timestamp {info?.StartTime}", ex);
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

        LogVerbose($"\n📅 EVENT: {info.Group}/{info.Metadata} at {info.StartTime}ms");

        var eventKey = $"{info.Group}/{info.Metadata}";
        if (!uniqueEvents.ContainsKey(eventKey))
        {
            uniqueEvents[eventKey] = new List<(string, string, int, uint)>();
        }
        uniqueEvents[eventKey].Add((info.Group, info.Metadata, info.SizeInBytes, info.StartTime));

        using var decryptedArchive = DecryptBuffer(archive, info.SizeInBytes);

        if (info.Group == ReplayEventTypes.PLAYER_ELIMINATION)
        {
            try
            {
                ParseAndStoreEliminationEvent(decryptedArchive, info);
            }
            catch (Exception ex)
            {
                LogVerbose($"❌ Error parsing elimination event: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Parse a player elimination event and store it
    /// </summary>
    private void ParseAndStoreEliminationEvent(FArchive archive, Unreal.Core.Models.EventInfo info)
    {
        var version = archive.ReadInt32();

        if (version >= 3)
        {
            archive.SkipBytes(1);

            if (version >= 6)
            {
                archive.ReadFQuat();  // EliminatedInfo.Rotation
                archive.ReadFVector();  // EliminatedInfo.Location
                archive.ReadFVector();  // EliminatedInfo.Scale
            }

            archive.ReadFQuat();  // EliminatorInfo.Rotation
            archive.ReadFVector();  // EliminatorInfo.Location
            archive.ReadFVector();  // EliminatorInfo.Scale
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

        // Parse eliminated player
        var eliminatedInfo = new PlayerEliminationInfo();
        ParsePlayer(archive, eliminatedInfo, version);

        // Parse eliminator
        var eliminatorInfo = new PlayerEliminationInfo();
        ParsePlayer(archive, eliminatorInfo, version);

        var gunType = archive.ReadByte();
        var knocked = archive.ReadUInt32AsBoolean();
        var time = info.StartTime / 1000.0;  // Convert ms to seconds

        _parsedEliminations.Add((
      eliminatedId: eliminatedInfo.Id ?? "Unknown",
      eliminatorId: eliminatorInfo.Id ?? "Unknown",
      time: time,
      knocked: knocked
  ));
    }


    void LogObject(string label, object obj, int indent = 0)
    {
        string indentStr = new string(' ', indent);

        if (obj == null)
        {
            LogVerbose($"{indentStr}{label}: null");
            return;
        }

        Type type = obj.GetType();
        LogVerbose($"{indentStr}{label}: {type.Name}");

        // Log all public properties
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            object value = null;
            try { value = prop.GetValue(obj); } catch { }

            if (value == null)
            {
                LogVerbose($"{indentStr}    {prop.Name}: null");
            }
            else if (prop.PropertyType.IsPrimitive || prop.PropertyType == typeof(string))
            {
                LogVerbose($"{indentStr}    {prop.Name}: {value}");
            }
            else
            {
                // Recursively log nested objects
                LogObject(prop.Name, value, indent + 4);
            }
        }

        // Log all public fields (if any)
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            object value = null;
            try { value = field.GetValue(obj); } catch { }

            if (value == null)
            {
                LogVerbose($"{indentStr}    {field.Name}: null");
            }
            else if (field.FieldType.IsPrimitive || field.FieldType == typeof(string))
            {
                LogVerbose($"{indentStr}    {field.Name}: {value}");
            }
            else
            {
                LogObject(field.Name, value, indent + 4);
            }
        }
    }

    private void ParseSessionMContent(FArchive archive)
    {
        LogVerbose("\n === PARSING SESSION M CONTENT === ");

        try
        {
            // SessionM is likely a serialized UObject or TArray
            // Try different parsing strategies

            // Strategy 1: Try as TArray of key-value pairs
            archive.Seek(0, System.IO.SeekOrigin.Begin);
            var count = archive.ReadInt32();
            LogVerbose($"First int32: {count}");

            if (count > 0 && count < 10000)
            {
                LogVerbose($"Attempting to read as array of {count} elements...\n");

                for (int i = 0; i < Math.Min(count, 100); i++) // Limit to 100 for safety
                {
                    try
                    {
                        // Try reading as FString key-value pair
                        var key = archive.ReadFString();

                        if (string.IsNullOrEmpty(key) || key.Length > 200)
                        {
                            LogVerbose($"[{i}] Invalid key, stopping parse");
                            break;
                        }

                        // Try to determine value type
                        // Could be FString, int32, float, etc.
                        var valueLength = archive.ReadInt32();

                        if (valueLength < 0)
                        {
                            // Negative means Unicode string
                            valueLength = -valueLength;
                            var valueBytes = archive.ReadBytes(valueLength * 2);
                            var value = System.Text.Encoding.Unicode.GetString(valueBytes.ToArray()).TrimEnd('\0');
                            LogVerbose($"[{i}] {key} = \"{value}\" (UTF-16)");
                        }
                        else if (valueLength > 0 && valueLength < 1000)
                        {
                            // Positive means ASCII string
                            var valueBytes = archive.ReadBytes(valueLength);
                            var value = System.Text.Encoding.ASCII.GetString(valueBytes.ToArray()).TrimEnd('\0');
                            LogVerbose($"[{i}] {key} = \"{value}\" (ASCII)");
                        }
                        else if (valueLength == 0)
                        {
                            // Empty string or might be a numeric value
                            // Try reading as different types
                            LogVerbose($"[{i}] {key} = (empty or different type)");
                        }
                        else
                        {
                            LogVerbose($"[{i}] {key} = (unknown type, length={valueLength})");
                            // Skip this entry
                            archive.Seek(Math.Min(valueLength, 1000), System.IO.SeekOrigin.Current);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogVerbose($"[{i}] Parse error: {ex.Message}");
                        break;
                    }
                }
            }
            else
            {
                LogVerbose("First int32 doesn't look like a valid array count");

                // Strategy 2: Scan for strings
                LogVerbose("\n=== SCANNING FOR STRINGS ===");
                archive.Seek(0, System.IO.SeekOrigin.Begin);

                // Read a reasonable buffer size - adjust as needed
                var bufferSize = 10 * 1024 * 1024; // 10MB max
                var allBytes = archive.ReadBytes(bufferSize);

                var strings = new List<(int offset, string text)>();
                var currentString = new List<char>();
                int stringStart = 0;

                for (int i = 0; i < allBytes.Length; i++)
                {
                    var b = allBytes[i]; // Direct indexing on ReadOnlySpan<byte>

                    if (b >= 32 && b < 127) // Printable ASCII
                    {
                        if (currentString.Count == 0)
                            stringStart = i;
                        currentString.Add((char) b);
                    }
                    else
                    {
                        if (currentString.Count >= 4)
                        {
                            strings.Add((stringStart, new string(currentString.ToArray())));
                        }
                        currentString.Clear();
                    }
                }

                LogVerbose($"Found {strings.Count} readable strings");
                LogVerbose("\nFirst 50 strings:");
                foreach (var (offset, text) in strings.Take(50))
                {
                    LogVerbose($"  @{offset:X6}: {text}");
                }

                // Look for JSON
                var fullText = System.Text.Encoding.ASCII.GetString(allBytes.ToArray(), 0, allBytes.Length);
                var jsonStart = fullText.IndexOf('{');
                if (jsonStart >= 0)
                {
                    LogVerbose($"\n[!] Found potential JSON at offset {jsonStart}");
                    var jsonSnippet = fullText.Substring(jsonStart, Math.Min(500, fullText.Length - jsonStart));
                    LogVerbose($"Preview: {jsonSnippet}");
                }
            }
        }
        catch (Exception ex)
        {
            LogVerbose($"ParseSessionMContent error: {ex.Message}");
            LogVerbose(ex.StackTrace);
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


    public virtual void ParsePlayer(FArchive archive, PlayerEliminationInfo info, int version)
    {
        var startPos = archive.Position;

        if (version < 6)
        {
            info.Id = archive.ReadFString();
            LogVerbose($"    [v<6] Read FString: '{info.Id}'");
            return;
        }

        // Read the type byte
        var typeByte = archive.ReadByte();
        info.PlayerType = (PlayerTypes) typeByte;

        LogVerbose($"    [v>=6] Type byte: {typeByte} ({info.PlayerType})");

        switch (info.PlayerType)
        {
            case PlayerTypes.BOT:
                info.Id = "Bot";
                break;

            case PlayerTypes.NAMED_BOT:
                var name = archive.ReadFString();
                LogVerbose($"      NAMED_BOT read: '{name}'");
                info.Id = name;
                break;

            case PlayerTypes.PLAYER:
                var guidSize = archive.ReadByte();
                var guid = archive.ReadGUID(guidSize);
                LogVerbose($"      PLAYER GUID (size {guidSize}): {guid}");
                info.Id = guid;
                break;

            default:
                LogVerbose($"      UNKNOWN TYPE: {typeByte}");
                info.Id = $"UNKNOWN_{typeByte}";
                break;
        }

        var bytesRead = archive.Position - startPos;
        LogVerbose($"    Total bytes read for player: {bytesRead}");
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

    // Add this to FortniteReplayReader.cs

    /// <summary>
    /// Unified player stats model - single source of truth for all exports
    /// Update this when adding/removing stats
    /// </summary>
    public class PlayerStatsExport
    {
        public string ReplayId { get; set; }
        public string EpicId { get; set; }
        public string PlayerName { get; set; }
        public int TeamIndex { get; set; }
        public int Eliminations { get; set; }
        public bool WasRebooted { get; set; }
        public double AliveSeconds { get; set; }
        public int ZonesSurvived { get; set; }

        // Building stats
        public int TotalBuildsPlaced { get; set; }
        public int WoodBuildsPlaced { get; set; }
        public int StoneBuildsPlaced { get; set; }
        public int MetalBuildsPlaced { get; set; }
        public int WallsPlaced { get; set; }
        public int FloorsPlaced { get; set; }
        public int StairsPlaced { get; set; }
        public int RoofsPlaced { get; set; }
        public int BuildsEdited { get; set; }
        public int BuildsDestroyed { get; set; }

        // Damage stats
        public double DamageDealt { get; set; }
        public double DamagePerMinute { get; set; }
        public double DamageTaken { get; set; }
        public int ShotsHit { get; set; }

        // Harvest stats
        public int TotalMaterialsHarvested { get; set; }
        public int WoodHarvested { get; set; }
        public int StoneHarvested { get; set; }
        public int MetalHarvested { get; set; }
        public int HarvestActions { get; set; }
    }

    /// <summary>
    /// Get all player stats in unified format
    /// </summary>
    private async Task<List<PlayerStatsExport>> GetAllPlayerStatsAsync(string replayId)
    {
        double matchStart = Builder.GetMatchStartTime();
        double matchEnd = Builder.GetCurrentTimeDouble();
        DamageTracker.SetMatchTimes(matchStart, matchEnd);

        var allPlayerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        allPlayerIds.UnionWith(playerRealtimeStats.Keys);
        allPlayerIds.UnionWith(DamageTracker.PlayerStats.Keys);
        allPlayerIds.UnionWith(HarvestTracker.GetAllPlayerStats().Keys);
        allPlayerIds.UnionWith(BuildTracker.GetAllPlayerStats().Keys);
        allPlayerIds.UnionWith(Builder.GetAllEliminationCounts().Keys);

        var playerStats = new List<PlayerStatsExport>();

        foreach (var playerId in allPlayerIds)
        {
            try
            {
                var playerName = GetPlayerNameFromReplay(playerId);
                var playerData = Builder.GetAllPlayers().FirstOrDefault(p => p.PlayerId.Equals(playerId, StringComparison.OrdinalIgnoreCase));
                var buildStats = BuildTracker.GetPlayerStats(playerId);
                var damageStats = DamageTracker.PlayerStats.ContainsKey(playerId) ? DamageTracker.PlayerStats[playerId] : null;
                var harvestStats = HarvestTracker.GetPlayerStats(playerId);
                var eliminations = Builder.GetEliminationCounts().ContainsKey(playerId) ? Builder.GetEliminationCounts()[playerId] : 0;

                var stats = new PlayerStatsExport
                {
                    ReplayId = replayId,
                    EpicId = playerId.ToLower(),
                    PlayerName = !string.IsNullOrEmpty(playerName) && !playerName.Equals(playerId, StringComparison.OrdinalIgnoreCase) ? playerName : string.Empty,
                    TeamIndex = playerData?.TeamIndex ?? 0,
                    Eliminations = eliminations,
                    WasRebooted = Builder.WasPlayerRebooted(playerId),
                    AliveSeconds = Builder.GetPlayerAliveTime(playerId),
                    ZonesSurvived = Builder.GetZonesSurvived(playerId),

                    TotalBuildsPlaced = buildStats.TotalBuildsPlaced,
                    WoodBuildsPlaced = buildStats.WoodBuilds,
                    StoneBuildsPlaced = buildStats.StoneBuilds,
                    MetalBuildsPlaced = buildStats.MetalBuilds,
                    WallsPlaced = buildStats.WallsPlaced,
                    FloorsPlaced = buildStats.FloorsPlaced,
                    StairsPlaced = buildStats.StairsPlaced,
                    RoofsPlaced = buildStats.RoofsPlaced,
                    BuildsEdited = buildStats.BuildsEdited,
                    BuildsDestroyed = buildStats.BuildsDestroyed,

                    DamageDealt = damageStats?.TotalDamageDealt ?? 0,
                    DamagePerMinute = DamageTracker.GetDamagePerMinute(playerId),
                    DamageTaken = damageStats?.TotalDamageTaken ?? 0,
                    ShotsHit = damageStats?.ShotsHit ?? 0,

                    TotalMaterialsHarvested = harvestStats.TotalMaterialsHarvested,
                    WoodHarvested = harvestStats.WoodHarvested,
                    StoneHarvested = harvestStats.StoneHarvested,
                    MetalHarvested = harvestStats.MetalHarvested,
                    HarvestActions = harvestStats.HarvestActions,
                };

                playerStats.Add(stats);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EXPORT_ERROR] Failed to process {playerId}: {ex.Message}");
            }
        }

        return playerStats;
    }

    /// <summary>
    /// Export to CSV using unified stats model
    /// </summary>
    public async Task ExportPlayerStatsToCSVAsync(string filePath)
    {
        var replayId = Path.GetFileNameWithoutExtension(filePath).Replace("player_stats_", "");
        var playerStats = await GetAllPlayerStatsAsync(replayId);

        var csvLines = new List<string>();

        // Headers from PlayerStatsExport properties
        var headers = new List<string>
    {
        nameof(PlayerStatsExport.ReplayId),
        nameof(PlayerStatsExport.EpicId),
        nameof(PlayerStatsExport.PlayerName),
        nameof(PlayerStatsExport.TeamIndex),
        nameof(PlayerStatsExport.Eliminations),
        nameof(PlayerStatsExport.WasRebooted),
        nameof(PlayerStatsExport.AliveSeconds),
        nameof(PlayerStatsExport.ZonesSurvived),
        nameof(PlayerStatsExport.TotalBuildsPlaced),
        nameof(PlayerStatsExport.WoodBuildsPlaced),
        nameof(PlayerStatsExport.StoneBuildsPlaced),
        nameof(PlayerStatsExport.MetalBuildsPlaced),
        nameof(PlayerStatsExport.WallsPlaced),
        nameof(PlayerStatsExport.FloorsPlaced),
        nameof(PlayerStatsExport.StairsPlaced),
        nameof(PlayerStatsExport.RoofsPlaced),
        nameof(PlayerStatsExport.BuildsEdited),
        nameof(PlayerStatsExport.BuildsDestroyed),
        nameof(PlayerStatsExport.DamageDealt),
        nameof(PlayerStatsExport.DamagePerMinute),
        nameof(PlayerStatsExport.DamageTaken),
        nameof(PlayerStatsExport.ShotsHit),
        nameof(PlayerStatsExport.TotalMaterialsHarvested),
        nameof(PlayerStatsExport.WoodHarvested),
        nameof(PlayerStatsExport.StoneHarvested),
        nameof(PlayerStatsExport.MetalHarvested),
        nameof(PlayerStatsExport.HarvestActions),
    };

        csvLines.Add(string.Join(",", headers));

        foreach (var stats in playerStats)
        {
            var row = new List<string>
        {
            stats.ReplayId,
            stats.EpicId,
            $"\"{stats.PlayerName}\"",
            stats.TeamIndex.ToString(),
            stats.Eliminations.ToString(),
            (stats.WasRebooted ? 1 : 0).ToString(),
            stats.AliveSeconds.ToString("F2"),
            stats.ZonesSurvived.ToString(),
            stats.TotalBuildsPlaced.ToString(),
            stats.WoodBuildsPlaced.ToString(),
            stats.StoneBuildsPlaced.ToString(),
            stats.MetalBuildsPlaced.ToString(),
            stats.WallsPlaced.ToString(),
            stats.FloorsPlaced.ToString(),
            stats.StairsPlaced.ToString(),
            stats.RoofsPlaced.ToString(),
            stats.BuildsEdited.ToString(),
            stats.BuildsDestroyed.ToString(),
            stats.DamageDealt.ToString("F2"),
            stats.DamagePerMinute.ToString("F2"),
            stats.DamageTaken.ToString("F2"),
            stats.ShotsHit.ToString(),
            stats.TotalMaterialsHarvested.ToString(),
            stats.WoodHarvested.ToString(),
            stats.StoneHarvested.ToString(),
            stats.MetalHarvested.ToString(),
            stats.HarvestActions.ToString(),
        };
            csvLines.Add(string.Join(",", row));
        }

        await File.WriteAllLinesAsync(filePath, csvLines);
        LogVerbose($"\n*** CSV exported: {filePath} ({playerStats.Count} players) ***");
    }

    /// <summary>
    /// Export to JSON using unified stats model - compact, production-ready format
    /// Returns JSON object instead of writing to file
    /// </summary>
    public async Task<object> ExportPlayerStatsToJsonAsync(string replayId)
    {
        var playerStats = await GetAllPlayerStatsAsync(replayId);

        var jsonOutput = new
        {
            replay_id = replayId,
            export_time = DateTime.UtcNow.ToString("O"),
            player_count = playerStats.Count,
            players = playerStats.Select(p => new
            {
                epic_id = p.EpicId,
                player_name = p.PlayerName,
                team = p.TeamIndex,
                stats = new
                {
                    eliminations = p.Eliminations,
                    rebooted = p.WasRebooted,
                    alive_seconds = Math.Round(p.AliveSeconds, 2),
                    zones_survived = p.ZonesSurvived,
                    builds = new
                    {
                        total = p.TotalBuildsPlaced,
                        wood = p.WoodBuildsPlaced,
                        stone = p.StoneBuildsPlaced,
                        metal = p.MetalBuildsPlaced,
                        walls = p.WallsPlaced,
                        floors = p.FloorsPlaced,
                        stairs = p.StairsPlaced,
                        roofs = p.RoofsPlaced,
                        edited = p.BuildsEdited,
                        destroyed = p.BuildsDestroyed,
                    },
                    damage = new
                    {
                        dealt = Math.Round(p.DamageDealt, 2),
                        per_minute = Math.Round(p.DamagePerMinute, 2),
                        taken = Math.Round(p.DamageTaken, 2),
                        shots_hit = p.ShotsHit,
                    },
                    harvesting = new
                    {
                        total = p.TotalMaterialsHarvested,
                        wood = p.WoodHarvested,
                        stone = p.StoneHarvested,
                        metal = p.MetalHarvested,
                        actions = p.HarvestActions,
                    }
                }
            }).ToList()
        };

        LogVerbose($"\n*** JSON exported: {playerStats.Count} players ***");
        return jsonOutput;
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