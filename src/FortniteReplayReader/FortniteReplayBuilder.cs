using FortniteReplayReader.Models;
using FortniteReplayReader.Models.NetFieldExports;
using FortniteReplayReader.Models.NetFieldExports.Weapons;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Unreal.Core.Models; // for NetworkGUID
using Unreal.Core.Contracts; // for INetFieldExportGroup
using System.Collections.Generic;
using System;
using FortniteReplayReader.Models.Eliminations;

namespace FortniteReplayReader;

/// <summary>
/// Responsible for constructing the <see cref="FortniteReplay"/> out of the received exports.
/// </summary>
public class FortniteReplayBuilder
{


    public class PlayerStateTimeline
    {
        public string PlayerId { get; set; }
        public List<StateChange> Changes { get; set; } = new();

        public PlayerStatus GetStatusAt(double time)
        {
            var change = Changes.Where(c => c.Time <= time).OrderByDescending(c => c.Time).FirstOrDefault();
            return change?.Status ?? PlayerStatus.Alive;
        }

        public List<PlayerStatus> GetStatusBetween(double startTime, double endTime)
        {
            return Changes
                .Where(c => c.Time > startTime && c.Time <= endTime)
                .Select(c => c.Status)
                .ToList();
        }

        public void AddChange(double time, PlayerStatus status, bool rebootCardAvailable = false, bool canBeRevived = false)
        {
            // ✅ Don't add duplicate states at the same time
            var existingAtTime = Changes.FirstOrDefault(c => c.Time == time && c.Status == status);
            if (existingAtTime != null)
                return;

            Changes.Add(new StateChange
            {
                Time = time,
                Status = status,
                RebootCardAvailable = rebootCardAvailable,
                CanBeRevived = canBeRevived
            });
            Changes = Changes.OrderBy(c => c.Time).ToList();
        }
    }

    public class StateChange
    {
        public double Time { get; set; }
        public PlayerStatus Status { get; set; }
        public bool RebootCardAvailable { get; set; }
        public bool CanBeRevived { get; set; }
    }

    private Dictionary<string, int> playerZonesSurvived = new();
    private float lastSafeZoneStartShrinkTime = -1;
    private bool firstZoneProcessed = false;

    private readonly GameData GameData = new();

    public double GetMatchStartTime()
    {
        return GameData?.MatchStartTime ?? 0;
    }

    private readonly MapData MapData = new();
    private readonly List<KillFeedEntry> KillFeed = new();

    private Dictionary<int, double> _teamFullyEliminatedTimes = new Dictionary<int, double>();

    private readonly Dictionary<string, string> _activeKnocks = new(); // victimId 
    private readonly Dictionary<uint, HealthSet> _previousHealthSets = new();
    private readonly Dictionary<uint, string> _channelToPlayerId = new();

    private Dictionary<uint, string> _buildChannelToPlayerId = new();

    private Dictionary<string, double> playerSpawnTimes = new();
    private Dictionary<string, double> playerDeathTimes = new();

    public readonly Dictionary<uint, uint> _actorToChannel = new();
    public readonly Dictionary<uint, uint> _channelToActor = new();

    private double? matchStartTime = null;
    private Dictionary<string, List<(double timestamp, string property, object value)>> _playerStateHistory = new();

    private readonly Dictionary<uint, uint> _pawnChannelToStateChannel = new();

    /// <summary>
    /// Sometimes we receive a PlayerPawn but we havent received the PlayerState yet, so we dont want to processes these yet.
    /// </summary>
    private readonly Dictionary<uint, List<QueuedPlayerPawn>> _queuedPlayerPawns = new();

    public List<KillFeedEntry> GetKillFeed()
    {
        return KillFeed;
    }
    private readonly HashSet<uint> _onlySpectatingPlayers = new();
    public readonly Dictionary<uint, PlayerData> _players = new();
    private readonly Dictionary<int?, TeamData> _teams = new();
    private readonly Dictionary<uint, Llama> _llamas = new();
    private readonly Dictionary<int, RebootVan> _rebootVans = new();
    private readonly Dictionary<uint, Models.SupplyDrop> _drops = new();

    private readonly Dictionary<uint, Inventory> _inventories = new();
    private readonly Dictionary<uint, WeaponData> _weapons = new();
    private readonly Dictionary<uint, WeaponData> _unknownWeapons = new();

    private Dictionary<string, PlayerInfo> _playerInfo;
    private Dictionary<uint, string> _actorIdToPlayerId;


    private readonly Dictionary<uint, string> _broadcastChannelToPlayerId = new();


    private float? ReplicatedWorldTimeSeconds = 0;
    private double? ReplicatedWorldTimeSecondsDouble = 0;

    private readonly Dictionary<NetworkGUID, INetFieldExportGroup> actorExportGroups
       = new Dictionary<NetworkGUID, INetFieldExportGroup>();


    // ========================================
    // ADD TO FortniteReplayBuilder CLASS
    // ========================================

    // Add these new fields to your existing class:
    private Dictionary<string, int> _eliminationCounts = new(); private readonly HashSet<string> _eliminatedPlayers = new();
    private readonly HashSet<string> _rebootedPlayers = new();
    private readonly List<EliminationEventData> _allEliminationEvents = new();

    // Helper class for detailed elimination tracking
    public class EliminationEventData
    {
        public string EliminatorId { get; set; }
        public string VictimId { get; set; }
        public bool IsKnock { get; set; }
        public bool IsSelfElim { get; set; }
        public float Timestamp { get; set; }
        public string KnockCreditTo { get; set; }
        public bool WasRebooted { get; set; }
    }

    public Dictionary<string, List<(double timestamp, string property, object value)>> GetPlayerStateHistory()
    {
        return _playerStateHistory;
    }

    private Dictionary<string, int> _eliminationCredits = new Dictionary<string, int>();

    public void SetEliminationCredits(Dictionary<string, int> credits)
    {
        _eliminationCredits = credits;
    }

    public Dictionary<string, int> GetEliminationCredits()
    {
        return _eliminationCredits;
    }

    public int GetEliminationCreditForPlayer(string playerId)
    {
        return _eliminationCredits.ContainsKey(playerId) ? _eliminationCredits[playerId] : 0;
    }

    public void TrackZoneSurvival(SafeZoneIndicator safeZone)
    {
        if (safeZone?.SafeZoneStartShrinkTime == null || safeZone.SafeZoneStartShrinkTime <= 0)
        {
            return;
        }

        float currentStartShrinkTime = safeZone.SafeZoneStartShrinkTime;

        Console.WriteLine($"[ZONE_DEBUG] SafeZoneStartShrinkTime: {currentStartShrinkTime}, Last: {lastSafeZoneStartShrinkTime}");

        // ✅ First time, set baseline AND count first zone
        if (lastSafeZoneStartShrinkTime == -1)
        {
            lastSafeZoneStartShrinkTime = currentStartShrinkTime;

            // Count first zone survivors
            if (!firstZoneProcessed)
            {
                firstZoneProcessed = true;
                int aliveCount = 0;
                foreach (var player in _players.Values)
                {
                    if (player.DeathTime == null)
                    {
                        string playerId = player.PlayerId;
                        if (!playerZonesSurvived.ContainsKey(playerId))
                            playerZonesSurvived[playerId] = 0;

                        playerZonesSurvived[playerId]++;
                        aliveCount++;
                    }
                }
                Console.WriteLine($"[ZONE_DEBUG] ⚠️ ZONE 1 - {aliveCount} players survived");
            }
            return;
        }

        // ✅ Zone changed
        if (currentStartShrinkTime != lastSafeZoneStartShrinkTime)
        {
            Console.WriteLine($"[ZONE_DEBUG] ⚠️ NEW ZONE: {lastSafeZoneStartShrinkTime} → {currentStartShrinkTime}");

            int aliveCount = 0;
            foreach (var player in _players.Values)
            {
                if (player.DeathTime == null)
                {
                    string playerId = player.PlayerId;
                    if (!playerZonesSurvived.ContainsKey(playerId))
                        playerZonesSurvived[playerId] = 0;

                    playerZonesSurvived[playerId]++;
                    aliveCount++;
                }
            }

            Console.WriteLine($"[ZONE_DEBUG] {aliveCount} players survived");
            lastSafeZoneStartShrinkTime = currentStartShrinkTime;
        }
    }
    public int GetZonesSurvived(string playerId)
    {
        return playerZonesSurvived.ContainsKey(playerId) ? playerZonesSurvived[playerId] : 0;
    }

    public Dictionary<string, int> GetAllZonesSurvived()
    {
        return playerZonesSurvived;
    }

    private readonly Dictionary<uint, (uint channelIndex, Actor actor)> _actorGuidRegistry = new();

    public void RegisterActorGuid(uint guidValue, uint channelIndex, Actor actor)
    {
        _actorGuidRegistry[guidValue] = (channelIndex, actor);
        Console.WriteLine($"[GUID_REGISTRY] Registered GUID {guidValue} → Channel {channelIndex}");
    }

    public bool TryGetActorFromGuid(uint guidValue, out Actor actor)
    {
        if (_actorGuidRegistry.TryGetValue(guidValue, out var result))
        {
            actor = result.actor;
            return true;
        }
        actor = null;
        return false;
    }

    private readonly HashSet<uint> _harvestedArchetypes = new();

    public void RegisterHarvestedArchetype(uint archetypeGuid)
    {
        _harvestedArchetypes.Add(archetypeGuid);
    }

    public bool IsHarvestedArchetype(uint archetypeGuid)
    {
        return _harvestedArchetypes.Contains(archetypeGuid);
    }

    private readonly Dictionary<uint, string> _archetypeGuidToMaterial = new();

    public void RegisterArchetypeMaterial(uint archetypeGuid, string material)
    {
        _archetypeGuidToMaterial[archetypeGuid] = material;
    }

    public bool TryGetMaterialFromArchetype(uint archetypeGuid, out string material)
    {
        return _archetypeGuidToMaterial.TryGetValue(archetypeGuid, out material);
    }


    public double GetPlayerAliveTime(string playerId)
    {
        Console.WriteLine($"[ALIVE_TIME_START] Getting alive time for {playerId}");

        if (!playerSpawnTimes.ContainsKey(playerId))
        {
            Console.WriteLine($"[ALIVE_TIME_DEBUG] {playerId} - No spawn time recorded");
            return 0;
        }

        double spawnTime = playerSpawnTimes[playerId];
        double matchStart = GameData.MatchStartTime ?? 0;
        Console.WriteLine($"[ALIVE_TIME_DEBUG] {playerId} - Initial spawn time: {spawnTime:F2}s, Match start: {matchStart:F2}s");

        // ✅ If spawned before match start, use match start as spawn time instead
        if (spawnTime < matchStart)
        {
            Console.WriteLine($"[ALIVE_TIME_DEBUG] {playerId} - Spawn island spawn ({spawnTime:F2}s < {matchStart:F2}s), using match start instead");
            spawnTime = matchStart;
        }

        // ✅ Use death time if available, otherwise use current time
        double deathTime;
        if (playerDeathTimes.ContainsKey(playerId))
        {
            deathTime = playerDeathTimes[playerId];
            double aliveTime = deathTime - spawnTime;
            Console.WriteLine($"[ALIVE_TIME_DEBUG] {playerId} - Dead: Spawn {spawnTime:F2}s → Death {deathTime:F2}s = {aliveTime:F2}s alive");
            return aliveTime;
        }
        else
        {
            deathTime = GetCurrentTimeDouble();
            double aliveTime = deathTime - spawnTime;
            Console.WriteLine($"[ALIVE_TIME_DEBUG] {playerId} - Alive: Spawn {spawnTime:F2}s → Now {deathTime:F2}s = {aliveTime:F2}s alive");
            return aliveTime;
        }
    }
    public void TrackPlayerSpawn(string playerId, double spawnTime)
    {
        playerSpawnTimes[playerId] = spawnTime;
    }

    public void TrackPlayerDeath(string playerId, double deathTime)
    {
        playerDeathTimes[playerId] = deathTime;
    }
    public void OnGameStateUpdate(GameState gameState)
    {
        // ✅ Detect match start from GameState
        if (gameState?.bReplicatedHasBegunPlay == true && matchStartTime == null)
        {
            matchStartTime = GameData.MatchStartTime;
            Console.WriteLine($"[MATCH_START] Match started at {matchStartTime:F2}s");
        }
    }


    public (Dictionary<string, PlayerStateTimeline> timelines, Dictionary<string, List<double>> validatedReboots)
        BuildPlayerTimelines(List<KillFeedEntry> killFeed)
    {
        var timelines = new Dictionary<string, PlayerStateTimeline>();
        var validatedReboots = new Dictionary<string, List<double>>();  // NEW: Store validated reboots here

        foreach (var (playerId, history) in _playerStateHistory)
        {
            var timeline = new PlayerStateTimeline { PlayerId = playerId };
            var sortedHistory = history.OrderBy(h => h.timestamp).ToList();
            var playerName = GetDisplayNameForPlayerId(playerId);

            Console.WriteLine($"\n📈 BUILDING TIMELINE FOR: {playerName}");
            Console.WriteLine($"   Total history entries: {sortedHistory.Count}");

            var knockCount = 0;
            var rebootCount = 0;
            var reviveCount = 0;
            bool? lastChipState = null;
            bool wasJustFinished = false;

            // NEW: Initialize reboot list for this player
            validatedReboots[playerId] = new List<double>();

            foreach (var (timestamp, property, value) in sortedHistory)
            {
                // Track bDBNO = true (player knocked)
                if (property == "bDBNO" && value is bool bdbno && bdbno == true)
                {
                    timeline.AddChange(timestamp, PlayerStatus.Knocked);
                    knockCount++;
                    wasJustFinished = false;
                    Console.WriteLine($"   [{knockCount}] KNOCK @ {timestamp:F2}s");
                }

                // Track bDBNO = false (player finished/eliminated OR revived)
                if (property == "bDBNO" && value is bool bdbnoFalse && bdbnoFalse == false)
                {
                    // 🔧 PRIMARY CHECK: Use DeathCause to determine if this is a revive or finish
                    var deathCauseEntry = sortedHistory
                        .Where(h => h.timestamp == timestamp && h.property == "DeathCause")
                        .FirstOrDefault();

                    int deathCause = deathCauseEntry.value is int ? (int) deathCauseEntry.value : 50;

                    var deathServerTimeEntry = sortedHistory
                        .Where(h => h.timestamp == timestamp && h.property == "DeathServerTime")
                        .FirstOrDefault();

                    ulong deathServerTime = deathServerTimeEntry.value is ulong ? (ulong) deathServerTimeEntry.value : 0;

                    var finisherOrDownerEntry = sortedHistory
                        .Where(h => h.timestamp == timestamp && h.property == "FinisherOrDowner")
                        .FirstOrDefault();

                    int finisherId = finisherOrDownerEntry.value is int ? (int) finisherOrDownerEntry.value : 0;

                    bool wasKnocked = timeline.Changes.Any(c => c.Time < timestamp && c.Status == PlayerStatus.Knocked);

                    // 🔧 LOGIC:
                    // DeathCause=50 (Unspecified) + DeathServerTime=0 + FinisherOrDowner=0 + was knocked = REVIVE
                    // Anything else with bDBNO=false = FINISH
                    if (deathCause == 50 && deathServerTime == 0 && finisherId == 0 && wasKnocked)
                    {
                        // This is a REVIVE
                        timeline.AddChange(timestamp, PlayerStatus.Alive);
                        reviveCount++;
                        Console.WriteLine($"   ✅ REVIVED @ {timestamp:F2}s (DeathCause={deathCause}, DeathServerTime={deathServerTime}, was knocked)");
                        wasJustFinished = false;
                    }
                    else if (deathCause != 50 || deathServerTime > 0 || finisherId > 0)
                    {
                        // Real death cause OR DeathServerTime set OR FinisherOrDowner set = FINISH
                        wasJustFinished = true;
                        Console.WriteLine($"   FINISHED @ {timestamp:F2}s (DeathCause={deathCause}, DeathServerTime={deathServerTime}, FinisherOrDowner={finisherId})");
                    }
                    else
                    {
                        // Edge case: bDBNO=false but no knock before this - ignore
                        Console.WriteLine($"   ⚠️  IGNORED @ {timestamp:F2}s (bDBNO=false but wasn't knocked before, DeathCause={deathCause})");
                    }
                }

                // Track bResurrectionChipAvailable: true -> false (chip used for reboot)
                if (property == "bResurrectionChipAvailable" && value is bool chipValue)
                {
                    // ✅ Only count as reboot if: was finished AND chip goes true -> false
                    if (wasJustFinished && lastChipState.HasValue && lastChipState.Value && !chipValue)
                    {
                        // Find the player's FINAL elimination in killfeed (where they're the victim)
                        var finalElim = killFeed
                            .Where(k => k.PlayerName == playerId && k.IsDowned != true)  // elimination, not knock
                            .OrderByDescending(k => k.ReplicatedWorldTimeSecondsDouble)
                            .FirstOrDefault();

                        // Only mark as rebooted if the chip use happens BEFORE their final elimination
                        if (finalElim == null || timestamp < (finalElim.ReplicatedWorldTimeSecondsDouble ?? 0))
                        {
                            // This reboot happens before final elim, so it's valid
                            timeline.AddChange(timestamp, PlayerStatus.Alive);
                            rebootCount++;

                            // NEW: Store this validated reboot
                            validatedReboots[playerId].Add(timestamp);

                            var elimTime = finalElim?.ReplicatedWorldTimeSecondsDouble ?? 0;
                            Console.WriteLine($"   ✅ REBOOT [{rebootCount}] @ {timestamp:F2}s (valid - before final elimination @ {elimTime:F2}s)");
                            wasJustFinished = false;
                        }
                        else
                        {
                            Console.WriteLine($"   ❌ REBOOT REJECTED @ {timestamp:F2}s (after final elimination @ {finalElim.ReplicatedWorldTimeSecondsDouble:F2}s)");
                        }
                    }

                    lastChipState = chipValue;
                }
            }

            Console.WriteLine($"   SUMMARY: {knockCount} knocks, {reviveCount} revives, {rebootCount} valid reboots");
            timelines[playerId] = timeline;
        }

        // 🔧 CRITICAL: Add events from kill feed with proper ordering
        Console.WriteLine($"\n🔧 ADDING KILL FEED EVENTS TO TIMELINES:");
        Console.WriteLine($"   killFeed is null? {killFeed == null}");
        Console.WriteLine($"   killFeed count: {killFeed?.Count ?? -1}");

        var eliminationsAdded = 0;
        var knocksAdded = 0;
        var knockerStatesAdded = 0;

        if (killFeed == null)
        {
            Console.WriteLine($"   ❌ ERROR: killFeed is NULL! Cannot add events.");
            return (timelines, validatedReboots);
        }

        // 🔧 CRITICAL: Sort kill feed by time, then by order in original feed (as tie breaker)
        // This ensures simultaneous events are processed in the order they appeared
        // ALSO: Process eliminations/finishes BEFORE knockdowns at the same timestamp
        Console.WriteLine($"   Starting to process {killFeed.Count} kill feed entries...");
        int processedCount = 0;

        // Create list with original indices to preserve feed order
        var indexedKillFeed = killFeed
            .Select((entry, index) => new { entry, index })
            .OrderBy(x => x.entry.ReplicatedWorldTimeSecondsDouble ?? 0.0)
            .ThenBy(x => x.entry.IsDowned == true ? 1 : 0)  // Process eliminations (IsDowned=false) BEFORE knockdowns (IsDowned=true)
            .ThenBy(x => x.index)  // Preserve original feed order as final tie breaker
            .ToList();

        foreach (var item in indexedKillFeed)
        {
            var killEntry = item.entry;
            var originalIndex = item.index;
            processedCount++;

            var victimId = killEntry.PlayerName;
            double eventTime = killEntry.ReplicatedWorldTimeSecondsDouble ?? 0.0;
            bool isDowned = killEntry.IsDowned == true;

            if (processedCount <= 5 || processedCount % 50 == 0)
            {
                Console.WriteLine($"   [ENTRY {originalIndex}] Processing @ {eventTime:F2}s: {GetDisplayNameForPlayerId(victimId)} (isDowned={isDowned})");
            }

            // Add victim state
            if (!string.IsNullOrEmpty(victimId))
            {
                if (!timelines.ContainsKey(victimId))
                {
                    timelines[victimId] = new PlayerStateTimeline { PlayerId = victimId };
                    if (!validatedReboots.ContainsKey(victimId))
                        validatedReboots[victimId] = new List<double>();
                }

                if (isDowned)  // Knockdown
                {
                    timelines[victimId].AddChange(eventTime, PlayerStatus.Knocked);
                    knocksAdded++;
                    if (processedCount <= 5 || processedCount % 50 == 0)
                    {
                        Console.WriteLine($"      Added Knocked @ {eventTime:F2}s for {GetDisplayNameForPlayerId(victimId)}");
                    }
                }
                else  // Elimination
                {
                    timelines[victimId].AddChange(eventTime, PlayerStatus.Dead);
                    eliminationsAdded++;
                    if (processedCount <= 5 || processedCount % 50 == 0)
                    {
                        Console.WriteLine($"      Added Dead @ {eventTime:F2}s for {GetDisplayNameForPlayerId(victimId)}");
                    }
                }
            }

            // Add knocker state - with refined dead knocker check
            var knockerId = ResolveActorIdToPlayerId(killEntry.FinisherOrDownerActorId ?? 0);
            if (!string.IsNullOrEmpty(knockerId))
            {
                if (!timelines.ContainsKey(knockerId))
                {
                    timelines[knockerId] = new PlayerStateTimeline { PlayerId = knockerId };
                    if (!validatedReboots.ContainsKey(knockerId))
                        validatedReboots[knockerId] = new List<double>();
                }

                // 🔧 REFINED: Only reject if knocker was dead BEFORE this event (0.01s before)
                var knockerStatusBefore = timelines[knockerId].GetStatusAt(eventTime - 0.01);

                if (knockerStatusBefore == PlayerStatus.Dead)
                {
                    // Knocker was already dead before this event - reject
                    if (processedCount <= 5 || processedCount % 50 == 0)
                    {
                        Console.WriteLine($"      REJECTED: {GetDisplayNameForPlayerId(knockerId)} was already Dead before {eventTime:F2}s");
                    }
                }
                else
                {
                    // Knocker was alive before this event - add Alive state
                    timelines[knockerId].AddChange(eventTime, PlayerStatus.Alive);
                    knockerStatesAdded++;
                    if (processedCount <= 5 || processedCount % 50 == 0)
                    {
                        Console.WriteLine($"      Added Alive @ {eventTime:F2}s for {GetDisplayNameForPlayerId(knockerId)}");
                    }
                }
            }
        }

        Console.WriteLine($"   ✅ Processing complete!");
        Console.WriteLine($"   Total entries processed: {processedCount}");
        Console.WriteLine($"   Total knockdowns added: {knocksAdded}");
        Console.WriteLine($"   Total eliminations added: {eliminationsAdded}");
        Console.WriteLine($"   Total knocker states added: {knockerStatesAdded}");

        // NEW: Print summary of validated reboots
        Console.WriteLine($"\n📊 VALIDATED REBOOTS SUMMARY:");
        int totalValidatedReboots = validatedReboots.Values.Sum(r => r.Count);
        Console.WriteLine($"   Total validated reboots: {totalValidatedReboots}");
        var playersWithReboots = validatedReboots.Where(r => r.Value.Count > 0).ToList();
        foreach (var (playerId, reboots) in playersWithReboots)
        {
            Console.WriteLine($"   - {GetDisplayNameForPlayerId(playerId)}: {reboots.Count} reboot(s) @ {string.Join(", ", reboots.Select(t => $"{t:F2}s"))}");
        }

        Console.WriteLine($"\n📊 FINAL TIMELINE STATE FOR CRITICAL PLAYERS:");
        var criticalPlayerKeywords = new[] { "xavi", "hvn void", "IAm Enough", "ark kirillian" };

        foreach (var keyword in criticalPlayerKeywords)
        {
            // Find playerId by partial name match
            var playerId = timelines.Keys.FirstOrDefault(pid =>
            {
                var name = GetDisplayNameForPlayerId(pid);
                return name != null && name.Contains(keyword, StringComparison.OrdinalIgnoreCase);
            });

            if (playerId != null && timelines.ContainsKey(playerId))
            {
                var timeline = timelines[playerId];
                var playerName = GetDisplayNameForPlayerId(playerId);
                Console.WriteLine($"   {playerName}:");

                if (timeline.Changes.Count == 0)
                {
                    Console.WriteLine($"      ⚠️  Changes list is EMPTY!");
                }
                else
                {
                    foreach (var change in timeline.Changes.OrderBy(c => c.Time))
                    {
                        Console.WriteLine($"      @ {change.Time:F2}s: {change.Status}");
                    }
                }
            }
            else
            {
                Console.WriteLine($"   {keyword}: NOT FOUND in timelines");
            }
        }

        // NEW: Return both timelines and validated reboots
        return (timelines, validatedReboots);
    }

    public void SetPlayerInfo(Dictionary<string, PlayerInfo> playerInfo)
    {
        _playerInfo = playerInfo;

        // Also build the ActorId lookup
        _actorIdToPlayerId = new Dictionary<uint, string>();
        if (playerInfo != null)
        {
            foreach (var kvp in playerInfo)
            {
                _actorIdToPlayerId[kvp.Value.ActorId] = kvp.Key;
            }
        }

        Console.WriteLine($"✅ Player info set: {playerInfo?.Count ?? 0} players, {_actorIdToPlayerId.Count} actor mappings");
    }



    /// <summary>
    /// Debug: Print detailed breakdown for a specific player
    /// </summary>
    public void DebugPlayerEliminations(string playerId)
    {
        Console.WriteLine($"\n{'=',80}");
        Console.WriteLine($"DEBUG: Elimination details for {playerId}");
        Console.WriteLine($"{'=',80}");

        var events = _allEliminationEvents
            .Where(e => e.KnockCreditTo == playerId || e.EliminatorId == playerId)
            .OrderBy(e => e.Timestamp)
            .ToList();

        foreach (var e in events)
        {
            var type = e.IsKnock ? "KNOCK" : "ELIM";
            var selfElim = e.IsSelfElim ? " (SELF)" : "";
            var credited = e.KnockCreditTo != e.EliminatorId ? $" [credited to {e.KnockCreditTo}]" : "";

            Console.WriteLine($"  {e.Timestamp:F1}s | {type}{selfElim} | " +
                            $"Eliminator: {e.EliminatorId} → Victim: {e.VictimId}{credited}");
        }

        var finalCount = GetEliminationCount(playerId);
        Console.WriteLine($"\nFinal Count: {finalCount}");
        Console.WriteLine($"{'=',80}\n");
    }

    // ========================================
    // HELPER METHOD: Check if player has alive teammates
    // ========================================

    private bool HasAliveTeammatesForPlayer(string playerId)
    {
        // Find the player's team
        var playerData = _players.Values.FirstOrDefault(p => p.PlayerId == playerId);
        if (playerData?.TeamIndex == null)
            return false;

        // Get all players on the same team
        var teammates = _players.Values
            .Where(p => p.TeamIndex == playerData.TeamIndex && p.PlayerId != playerId)
            .ToList();

        // Check if any teammate is NOT eliminated
        foreach (var teammate in teammates)
        {
            if (!_eliminatedPlayers.Contains(teammate.PlayerId))
            {
                return true;
            }
        }

        return false;
    }

    // ========================================
    // METHOD: Get elimination count for a player
    // ========================================

    /// <summary>
    /// Get the elimination count for a player (with proper knock-credit logic applied)
    /// </summary>
    public int GetEliminationCount(string playerId)
    {
        return _eliminationCounts.TryGetValue(playerId, out var count) ? count : 0;
    }

    /// <summary>
    /// Get all elimination counts
    /// </summary>
    public IReadOnlyDictionary<string, int> GetAllEliminationCounts()
    {
        return _eliminationCounts;
    }

    /// <summary>
    /// Check if a player was rebooted during the match
    /// </summary>
    public bool WasPlayerRebooted(string playerId)
    {
        return _rebootedPlayers.Contains(playerId);
    }

    // ========================================
    // METHOD: Print elimination summary
    // ========================================

    /// <summary>
    /// Print a summary of all eliminations with knock-credit attribution
    /// Call this at the end of replay processing
    /// </summary>
    public void PrintEliminationSummary()
    {
        Console.WriteLine("\n" + new string('=', 80));
        Console.WriteLine("📊 ELIMINATION SUMMARY (with Fortnite knock-credit logic)");
        Console.WriteLine(new string('=', 80));

        var totalKnocks = _allEliminationEvents.Count(e => e.IsKnock);
        var totalElims = _allEliminationEvents.Count(e => !e.IsKnock);
        var totalSelfElims = _allEliminationEvents.Count(e => e.IsSelfElim);
        var creditTransfers = _allEliminationEvents.Count(e => !e.IsKnock && e.EliminatorId != e.KnockCreditTo);
        var totalReboots = _rebootedPlayers.Count;

        Console.WriteLine($"\n📈 Event Counts:");
        Console.WriteLine($"  Total Events: {_allEliminationEvents.Count}");
        Console.WriteLine($"  Knocks: {totalKnocks}");
        Console.WriteLine($"  Eliminations: {totalElims}");
        Console.WriteLine($"  Self-Eliminations: {totalSelfElims}");
        Console.WriteLine($"  Counted Kills: {_allEliminationEvents.Count(e => !e.IsKnock && !e.IsSelfElim)}");
        Console.WriteLine($"  Credit Transfers (knocker ≠ finisher): {creditTransfers}");
        Console.WriteLine($"  Reboots Detected: {totalReboots}");

        // Show rebooted players
        if (_rebootedPlayers.Any())
        {
            Console.WriteLine($"\n🔄 Rebooted Players:");
            foreach (var playerId in _rebootedPlayers)
            {
                var playerData = _players.Values.FirstOrDefault(p => p.PlayerId == playerId);
                var playerName = playerData?.PlayerName ?? playerId;
                Console.WriteLine($"  {playerName} ({playerId})");
            }
        }

        // Show unresolved knocks
        if (_activeKnocks.Any())
        {
            Console.WriteLine($"\n⚠️  Unresolved Knocks: {_activeKnocks.Count}");
            Console.WriteLine("     (Players knocked but never eliminated/revived before match ended)");
            foreach (var kvp in _activeKnocks)
            {
                var victimData = _players.Values.FirstOrDefault(p => p.PlayerId == kvp.Key);
                var knockerData = _players.Values.FirstOrDefault(p => p.PlayerId == kvp.Value);
                Console.WriteLine($"     {knockerData?.PlayerName ?? kvp.Value} knocked {victimData?.PlayerName ?? kvp.Key}");
            }
        }

        if (_eliminationCounts.Any())
        {
            Console.WriteLine("\n🎯 ELIMINATIONS LEADERBOARD:");
            var sortedPlayers = _eliminationCounts
                .OrderByDescending(x => x.Value)
                .ThenBy(x => x.Key);

            int rank = 1;
            foreach (var kvp in sortedPlayers)
            {
                var playerId = kvp.Key;
                var playerData = _players.Values.FirstOrDefault(p => p.PlayerId == playerId);
                var playerName = playerData?.PlayerName ?? playerId;

                // Count how many were direct vs credit transfers
                var directKills = _allEliminationEvents.Count(e =>
                    !e.IsKnock && !e.IsSelfElim &&
                    e.KnockCreditTo == playerId &&
                    e.EliminatorId == playerId);

                var creditedKills = _allEliminationEvents.Count(e =>
                    !e.IsKnock && !e.IsSelfElim &&
                    e.KnockCreditTo == playerId &&
                    e.EliminatorId != playerId);

                var killText = $"{kvp.Value} elimination{(kvp.Value != 1 ? "s" : "")}";
                if (creditedKills > 0)
                {
                    killText += $" ({directKills} finished own, {creditedKills} finished by others)";
                }

                // Check if player was rebooted
                if (_rebootedPlayers.Contains(playerId))
                {
                    killText += " 🔄";
                }

                Console.WriteLine($"  #{rank}. {playerName}: {killText}");
                rank++;
            }
        }
        else
        {
            Console.WriteLine("\n⚠️  No eliminations counted");
        }

        // Show examples of credit transfers
        var transferExamples = _allEliminationEvents
            .Where(e => !e.IsKnock && !e.IsSelfElim && e.EliminatorId != e.KnockCreditTo)
            .Take(5)
            .ToList();

        if (transferExamples.Any())
        {
            Console.WriteLine("\n🔄 Knock Credit Transfer Examples:");
            foreach (var ex in transferExamples)
            {
                var knockerData = _players.Values.FirstOrDefault(p => p.PlayerId == ex.KnockCreditTo);
                var finisherData = _players.Values.FirstOrDefault(p => p.PlayerId == ex.EliminatorId);
                Console.WriteLine($"  {knockerData?.PlayerName ?? ex.KnockCreditTo} knocked → " +
                                $"{finisherData?.PlayerName ?? ex.EliminatorId} finished → " +
                                $"Kill credited to {knockerData?.PlayerName ?? ex.KnockCreditTo}");
            }
        }

        Console.WriteLine("\n" + new string('=', 80) + "\n");
    }


    public void MapBuildChannelToPlayer(uint buildChannelIndex, string playerId)
    {
        Console.WriteLine("[DEBUG] Current BuildChannel → PlayerId mappings:");

        foreach (var kvp in _buildChannelToPlayerId)
        {
            Console.WriteLine($"  [DEBUG] Channel {kvp.Key} → Player {kvp.Value}");
        }

        Console.WriteLine($"[DEBUG] Mapping buildChannelIndex {buildChannelIndex} to playerId '{playerId}'");

        if (!string.IsNullOrEmpty(playerId))
        {
            _buildChannelToPlayerId[buildChannelIndex] = playerId;
            Console.WriteLine($"[DEBUG] Mapped successfully: {buildChannelIndex} → {playerId}");
        }
        else
        {
            Console.WriteLine($"[DEBUG] Skipped mapping for channel {buildChannelIndex} (playerId was null or empty)");
        }
    }




    /// <summary>
    /// Print all elimination events in chronological order for debugging
    /// </summary>
    public void PrintChronologicalTimeline()
    {
        Console.WriteLine("\n" + new string('=', 80));
        Console.WriteLine("📅 CHRONOLOGICAL ELIMINATION TIMELINE");
        Console.WriteLine(new string('=', 80) + "\n");

        var sortedEvents = _allEliminationEvents
            .OrderBy(e => e.Timestamp)
            .ThenBy(e => e.VictimId) // Secondary sort for same-time events
            .ToList();

        int eventNumber = 1;

        foreach (var evt in sortedEvents)
        {
            var elimType = evt.IsKnock ? "🥊 KNOCK" : "💀 ELIM";
            var selfTag = evt.IsSelfElim ? " [SELF]" : "";
            var rebootTag = evt.WasRebooted ? " [REBOOTED]" : "";

            var eliminatorData = _players.Values.FirstOrDefault(p => p.PlayerId == evt.EliminatorId);
            var victimData = _players.Values.FirstOrDefault(p => p.PlayerId == evt.VictimId);
            var knockerData = _players.Values.FirstOrDefault(p => p.PlayerId == evt.KnockCreditTo);

            var elimName = eliminatorData?.PlayerName ?? evt.EliminatorId;
            var victimName = victimData?.PlayerName ?? evt.VictimId;
            var knockerName = knockerData?.PlayerName ?? evt.KnockCreditTo;

            Console.WriteLine($"{eventNumber:3}. [{evt.Timestamp:F2}s] {elimType}{selfTag}{rebootTag}");
            Console.WriteLine($"     Eliminator: {elimName}");
            Console.WriteLine($"     Victim: {victimName}");

            if (!evt.IsKnock && evt.EliminatorId != evt.KnockCreditTo)
            {
                Console.WriteLine($"     📌 Credit → {knockerName} (original knocker)");
            }

            Console.WriteLine();
            eventNumber++;
        }

        Console.WriteLine(new string('=', 80));
        Console.WriteLine($"Total Events: {sortedEvents.Count}\n");
    }

    /// <summary>
    /// Print timeline for a specific player to trace their eliminations
    /// </summary>
    public void PrintPlayerTimeline(string targetPlayerId)
    {
        Console.WriteLine("\n" + new string('=', 80));
        Console.WriteLine($"📅 TIMELINE FOR PLAYER: {targetPlayerId}");
        Console.WriteLine(new string('=', 80) + "\n");

        var playerData = _players.Values.FirstOrDefault(p => p.PlayerId == targetPlayerId);
        var playerName = playerData?.PlayerName ?? targetPlayerId;
        Console.WriteLine($"Player: {playerName}\n");

        // All events involving this player (as eliminator or victim)
        var playerEvents = _allEliminationEvents
            .Where(e => e.EliminatorId == targetPlayerId || e.VictimId == targetPlayerId)
            .OrderBy(e => e.Timestamp)
            .ToList();

        int eventNumber = 1;

        foreach (var evt in playerEvents)
        {
            var role = evt.EliminatorId == targetPlayerId ? "ATTACKER" : "VICTIM";
            var elimType = evt.IsKnock ? "KNOCK" : "ELIM";
            var selfTag = evt.IsSelfElim ? " [SELF]" : "";
            var rebootTag = evt.WasRebooted ? " [REBOOTED]" : "";

            var otherData = evt.EliminatorId == targetPlayerId
                ? _players.Values.FirstOrDefault(p => p.PlayerId == evt.VictimId)
                : _players.Values.FirstOrDefault(p => p.PlayerId == evt.EliminatorId);

            var otherName = otherData?.PlayerName ?? (evt.EliminatorId == targetPlayerId ? evt.VictimId : evt.EliminatorId);

            Console.WriteLine($"{eventNumber:3}. [{evt.Timestamp:F2}s] {role} - {elimType}{selfTag}{rebootTag}");

            if (evt.EliminatorId == targetPlayerId)
            {
                Console.WriteLine($"     Eliminated: {otherName}");
                if (!evt.IsKnock && evt.KnockCreditTo != targetPlayerId)
                {
                    var knockerData = _players.Values.FirstOrDefault(p => p.PlayerId == evt.KnockCreditTo);
                    var knockerName = knockerData?.PlayerName ?? evt.KnockCreditTo;
                    Console.WriteLine($"     ⚠️  Kill credited to: {knockerName} (original knocker)");
                }
            }
            else
            {
                Console.WriteLine($"     Eliminated by: {otherName}");
            }

            Console.WriteLine();
            eventNumber++;
        }

        // Summary for this player
        var playerKills = _eliminationCounts.TryGetValue(targetPlayerId, out var killCount) ? killCount : 0;
        Console.WriteLine(new string('=', 80));
        Console.WriteLine($"Kill Count for {playerName}: {playerKills}");
        Console.WriteLine(new string('=', 80) + "\n");
    }

    /// <summary>
    /// Print events within a specific time window
    /// </summary>
    public void PrintTimeWindowEvents(float startTime, float endTime)
    {
        Console.WriteLine("\n" + new string('=', 80));
        Console.WriteLine($"📅 EVENTS BETWEEN {startTime:F2}s and {endTime:F2}s");
        Console.WriteLine(new string('=', 80) + "\n");

        var windowEvents = _allEliminationEvents
            .Where(e => e.Timestamp >= startTime && e.Timestamp <= endTime)
            .OrderBy(e => e.Timestamp)
            .ToList();

        int eventNumber = 1;

        foreach (var evt in windowEvents)
        {
            var elimType = evt.IsKnock ? "🥊 KNOCK" : "💀 ELIM";
            var selfTag = evt.IsSelfElim ? " [SELF]" : "";

            var eliminatorData = _players.Values.FirstOrDefault(p => p.PlayerId == evt.EliminatorId);
            var victimData = _players.Values.FirstOrDefault(p => p.PlayerId == evt.VictimId);

            var elimName = eliminatorData?.PlayerName ?? evt.EliminatorId;
            var victimName = victimData?.PlayerName ?? evt.VictimId;

            Console.WriteLine($"{eventNumber:3}. [{evt.Timestamp:F2}s] {elimType}{selfTag}");
            Console.WriteLine($"     {elimName} → {victimName}");
            Console.WriteLine();
            eventNumber++;
        }

        Console.WriteLine(new string('=', 80));
        Console.WriteLine($"Events in window: {windowEvents.Count}\n");
    }



    /// <summary>
    /// Link a broadcast channel to a player ID
    /// </summary>
    public void LinkBroadcastChannelToPlayer(uint broadcastChannel, string playerId)
    {
        if (!string.IsNullOrEmpty(playerId))
        {
            _broadcastChannelToPlayerId[broadcastChannel] = playerId;
        }
    }





    /// <summary>
    /// Get player ID from any channel type (actor ID, state channel, pawn channel, or broadcast)
    /// </summary>
    public string GetPlayerIdFromAnyChannel(uint channelIndex)
    {
        // Method 0: Build channel lookup (NEW)
        if (_buildChannelToPlayerId.TryGetValue(channelIndex, out var buildPlayerId))
        {
            Console.WriteLine($"[PLAYER_LOOKUP] Channel {channelIndex} → Method 0 (buildChannel) → {buildPlayerId}");
            return buildPlayerId;
        }

        // Method 1: Direct lookup in players (state channels)
        if (_players.TryGetValue(channelIndex, out var playerData))
        {
            Console.WriteLine($"[PLAYER_LOOKUP] Channel {channelIndex} → Method 1 (players) → {playerData.PlayerId}");
            return playerData.PlayerId ?? string.Empty;
        }

        // Method 2: Actor ID → Channel lookup
        if (_actorToChannel.TryGetValue(channelIndex, out var pawnChannel))
        {
            if (_pawnChannelToStateChannel.TryGetValue(pawnChannel, out var stateChannel))
            {
                if (_players.TryGetValue(stateChannel, out playerData))
                {
                    Console.WriteLine($"[PLAYER_LOOKUP] Channel {channelIndex} → Method 2a (actorToChannel→pawnToState) → {playerData.PlayerId}");
                    return playerData.PlayerId ?? string.Empty;
                }
            }
            if (_players.TryGetValue(pawnChannel, out playerData))
            {
                Console.WriteLine($"[PLAYER_LOOKUP] Channel {channelIndex} → Method 2b (actorToChannel direct) → {playerData.PlayerId}");
                return playerData.PlayerId ?? string.Empty;
            }
        }

        // Method 3: Pawn→State mapping
        if (_pawnChannelToStateChannel.TryGetValue(channelIndex, out var stateChannel2))
        {
            if (_players.TryGetValue(stateChannel2, out playerData))
            {
                Console.WriteLine($"[PLAYER_LOOKUP] Channel {channelIndex} → Method 3 (pawnToState) → {playerData.PlayerId}");
                return playerData.PlayerId ?? string.Empty;
            }
        }

        // Method 4: Broadcast→Player mapping
        if (_broadcastChannelToPlayerId.TryGetValue(channelIndex, out var playerId))
        {
            Console.WriteLine($"[PLAYER_LOOKUP] Channel {channelIndex} → Method 4 (broadcast) → {playerId}");
            return playerId;
        }

        Console.WriteLine($"[PLAYER_LOOKUP] Channel {channelIndex} → NO MATCH (all methods failed)");
        return string.Empty;
    }


    /// <summary>
    /// Registers an export group for an actor.
    /// Call this whenever a new export group is created or updated.
    /// </summary>
    public void RegisterExportGroup(NetworkGUID actor, INetFieldExportGroup exportGroup)
    {
        if (exportGroup != null)
        {
            actorExportGroups[actor] = exportGroup;
        }
    }

    /// <summary>
    /// Returns the export group for a given actor GUID, or null if none exists.
    /// </summary>
    public INetFieldExportGroup? GetExportGroup(NetworkGUID actor)
    {
        if (actorExportGroups.TryGetValue(actor, out var group))
            return group;

        return null;
    }

    public void AddActorChannel(uint channelIndex, uint guid)
    {
        _actorToChannel[guid] = channelIndex;
        _channelToActor[channelIndex] = guid;
    }

    public void RemoveChannel(uint channelIndex)
    {
        _weapons.Remove(channelIndex);
        _unknownWeapons.Remove(channelIndex);
    }

    public void ProcessEliminationsFromKillFeed()
    {
        // ✅ Get the match start time (when spawn island ends)
        double matchStartOffset = GameData.MatchStartTime ?? 0;

        Console.WriteLine($"\n📊 KILL FEED PROCESSING:");
        Console.WriteLine($"   Total entries: {KillFeed.Count}");
        Console.WriteLine($"   Match started at: {matchStartOffset:F2}s (real game time starts from {matchStartOffset:F2}s)");

        // Deduplicate first
        var dedupedFeed = new Dictionary<(string victim, double time, bool isDowned), KillFeedEntry>();
        foreach (var entry in KillFeed)
        {
            bool isDowned = entry.IsDowned == true;
            var key = (entry.PlayerName, entry.ReplicatedWorldTimeSecondsDouble ?? 0.0, isDowned);
            if (!dedupedFeed.ContainsKey(key))
            {
                dedupedFeed[key] = entry;
            }
        }

        var uniqueKillFeed = dedupedFeed.Values.OrderBy(e => e.ReplicatedWorldTimeSecondsDouble).ToList();
        Console.WriteLine($"   After dedup: {uniqueKillFeed.Count} unique entries (removed {KillFeed.Count - uniqueKillFeed.Count} duplicates)");

        // 🔧 DEBUG: Print raw deduped feed
        Console.WriteLine($"\n📋 RAW DEDUPED KILL FEED ({uniqueKillFeed.Count} entries):");
        foreach (var entry in uniqueKillFeed)
        {
            double eventTime = entry.ReplicatedWorldTimeSecondsDouble ?? 0.0;
            double realMatchTime = eventTime - matchStartOffset;
            bool isDowned = entry.IsDowned == true;
            var eventType = isDowned ? "KNOCK" : "ELIM";
            var finisher = ResolveActorIdToPlayerId(entry.FinisherOrDownerActorId ?? 0);
            var finisherName = finisher != null ? GetDisplayNameForPlayerId(finisher) : "UNKNOWN";
            Console.WriteLine($"   [{eventType}] @ {realMatchTime:F2}s (game {eventTime:F2}s): {GetDisplayNameForPlayerId(entry.PlayerName)} by {finisherName}");
        }

        // Build player state timelines from FortPlayerState history
        var (timelines, rebootTimeline) = BuildPlayerTimelines(uniqueKillFeed);

        BuildTeamElimTimeline(timelines);

        // Track all knocks for each victim (time-ordered)
        var knocksByVictim = new Dictionary<string, List<(string knockerId, double knockTime)>>();
        var processedEliminations = new HashSet<string>();

        // PASS 1: Record all knocks in chronological order
        Console.WriteLine($"\n📍 PASS 1: Recording all knocks...");
        foreach (var entry in uniqueKillFeed)
        {
            var victimId = entry.PlayerName;
            if (string.IsNullOrEmpty(victimId))
                continue;

            bool isKnock = entry.IsDowned == true;
            if (!isKnock)
                continue;

            double eventTime = entry.ReplicatedWorldTimeSecondsDouble ?? 0.0;
            double realMatchTime = eventTime - matchStartOffset;
            var knockerId = ResolveActorIdToPlayerId(entry.FinisherOrDownerActorId ?? 0);

            if (knockerId != null)
            {
                if (!knocksByVictim.ContainsKey(victimId))
                    knocksByVictim[victimId] = new List<(string, double)>();

                knocksByVictim[victimId].Add((knockerId, eventTime));
                Console.WriteLine($"   ✅ KNOCK @ {realMatchTime:F2}s (game time {eventTime:F2}s): {GetDisplayNameForPlayerId(knockerId)} knocked {GetDisplayNameForPlayerId(victimId)}");
            }
            else
            {
                Console.WriteLine($"   ❌ KNOCK @ {realMatchTime:F2}s: FAILED TO RESOLVE KNOCKER for {GetDisplayNameForPlayerId(victimId)}");
                Console.WriteLine($"      FinisherOrDownerActorId: {entry.FinisherOrDownerActorId}");
            }
        }

        // SINGLE PASS: Evaluate ALL eliminations with unified logic and look-ahead
        Console.WriteLine($"\n📍 EVALUATING ALL ELIMINATIONS:");
        int totalEliminations = 0;
        int totalCreditsAwarded = 0;

        foreach (var entry in uniqueKillFeed)
        {
            var victimId = entry.PlayerName;
            if (string.IsNullOrEmpty(victimId))
                continue;

            bool isKnock = entry.IsDowned == true;
            if (isKnock)
                continue;

            double eventTime = entry.ReplicatedWorldTimeSecondsDouble ?? 0.0;
            double realMatchTime = eventTime - matchStartOffset;
            var elimKey = $"{victimId}_{eventTime}";

            // Skip if already processed
            if (processedEliminations.Contains(elimKey))
                continue;

            totalEliminations++;

            // Record the elimination state in timeline
            if (timelines.ContainsKey(victimId))
            {
                timelines[victimId].AddChange(eventTime, PlayerStatus.Dead);
            }

            var finisherId = ResolveActorIdToPlayerId(entry.FinisherOrDownerActorId ?? 0);
            string creditTo = null;
            string creditReason = "";

            // 🔧 CRITICAL CHECK: Verify finisher is not dead BEFORE ANY LOGIC
            bool finisherIsDead = false;
            if (finisherId != null && timelines.ContainsKey(finisherId))
            {
                var finisherStatus = timelines[finisherId].GetStatusAt(eventTime);

                if (finisherStatus == PlayerStatus.Dead)
                {
                    Console.WriteLine($"   ❌ FINISHER DEAD: {GetDisplayNameForPlayerId(finisherId)} was dead at {eventTime:F2}s");
                    Console.WriteLine($"      Cannot credit dead player with elimination of {GetDisplayNameForPlayerId(victimId)}");
                    finisherIsDead = true;
                    creditReason = "finisher_was_dead";
                }
            }

            // 🔧 UNIFIED LOGIC WITH LOOK-AHEAD AND REBOOT HANDLING: Determine who gets credit

            // If finisher is dead, skip all logic and reject this elimination
            if (finisherIsDead)
            {
                creditTo = null;
                // Mark as processed and move to next
                processedEliminations.Add(elimKey);
                Console.WriteLine($"   ⏭️  SKIPPED: Finisher was dead, no credit assigned\n");
                continue;
            }

            // Case 1: Victim has knock(s) → Find the most recent VALID knock before this elimination
            if (knocksByVictim.ContainsKey(victimId) && knocksByVictim[victimId].Count > 0)
            {
                // 🔍 LOOK-AHEAD: Find the most recent knock that hasn't been superseded
                // AND hasn't been cleared by a reboot
                var knocksBeforeElim = knocksByVictim[victimId]
                    .Where(k => k.knockTime < eventTime)
                    .OrderByDescending(k => k.knockTime)
                    .ToList();

                if (knocksBeforeElim.Count > 0)
                {
                    // 🔧 CRITICAL FIX: Find the most recent knock that was NOT cleared by a reboot
                    (string knockerId, double knockTime)? validKnock = null;

                    foreach (var (candidateKnockerId, candidateKnockTime) in knocksBeforeElim)
                    {
                        // Check if victim was rebooted between this knock and the elimination
                        bool wasRevivedOrRebooted = WasRevivedOrRebooted(victimId, candidateKnockTime, eventTime, rebootTimeline);

                        Console.WriteLine($"   [DEBUG REVIVAL/REBOOT CHECK] Knock @ {candidateKnockTime:F2}s, Elim @ {eventTime:F2}s: wasRevivedOrRebooted={wasRevivedOrRebooted}");

                        if (!wasRevivedOrRebooted)
                        {
                            // This knock is valid (no reboot between knock and elimination)
                            validKnock = (candidateKnockerId, candidateKnockTime);
                            break;  // Take the most recent valid knock
                        }
                        else
                        {
                            Console.WriteLine($"   ℹ️  Knock at {candidateKnockTime:F2}s was cleared by reboot, checking earlier knocks...");
                        }
                    }

                    if (validKnock.HasValue)
                    {
                        var (knockerId, knockTime) = validKnock.Value;

                        // 🔧 KEY FIX: Check if there's another knock AFTER this one but BEFORE elimination
                        // If so, this knock was cleared and we should skip it
                        bool knockWasSuperseded = knocksBeforeElim.Where(k => k.knockTime > knockTime && k.knockTime < eventTime).Any();

                        if (knockWasSuperseded)
                        {
                            Console.WriteLine($"   ⚠️  ELIM @ {realMatchTime:F2}s: {GetDisplayNameForPlayerId(victimId)} → Valid knock was superseded by another knock, skipping knocker credit");
                            creditReason = "knock_superseded";
                        }
                        else
                        {
                            // 🔧 UPDATED: Validate the knocker using EvaluateEliminationCredit
                            // This function NOW includes the team elimination check
                            string knockResult = EvaluateEliminationCredit(victimId, eventTime, knocksByVictim, timelines, rebootTimeline);

                            if (knockResult != null)
                            {
                                creditTo = knockResult;
                                creditReason = "knocker";
                                Console.WriteLine($"   ✅ ELIM @ {realMatchTime:F2}s: {GetDisplayNameForPlayerId(victimId)} ✅ Credit to knocker {GetDisplayNameForPlayerId(creditTo)}");
                            }
                            else
                            {
                                Console.WriteLine($"   ⚠️  ELIM @ {realMatchTime:F2}s: {GetDisplayNameForPlayerId(victimId)} → Knocker validation failed, checking fallback to finisher");
                                creditReason = "knocker_validation_failed";

                                // Fall through to finisher check
                                if (finisherId != null && timelines.ContainsKey(finisherId))
                                {
                                    var finisherStatus = timelines[finisherId].GetStatusAt(eventTime);

                                    if (finisherStatus == PlayerStatus.Alive)
                                    {
                                        creditTo = finisherId;
                                        creditReason = "finisher_fallback";
                                        Console.WriteLine($"   ✅ ELIM @ {realMatchTime:F2}s: {GetDisplayNameForPlayerId(victimId)} → Fallback: Credit to finisher {GetDisplayNameForPlayerId(creditTo)}");
                                    }
                                    else
                                    {
                                        Console.WriteLine($"   ❌ ELIM @ {realMatchTime:F2}s: {GetDisplayNameForPlayerId(victimId)} → Knocker failed AND finisher dead");
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        // All knocks were rebooted/cleared
                        Console.WriteLine($"   ⚠️  ELIM @ {realMatchTime:F2}s: {GetDisplayNameForPlayerId(victimId)} → All previous knocks were cleared by reboots");
                        creditReason = "all_knocks_rebooted";

                        // Fall through to finisher check
                        if (finisherId != null && timelines.ContainsKey(finisherId))
                        {
                            var finisherStatus = timelines[finisherId].GetStatusAt(eventTime);

                            if (finisherStatus == PlayerStatus.Alive)
                            {
                                creditTo = finisherId;
                                creditReason = "finisher_fallback";
                                Console.WriteLine($"   ✅ ELIM @ {realMatchTime:F2}s: {GetDisplayNameForPlayerId(victimId)} → Fallback: Credit to finisher {GetDisplayNameForPlayerId(creditTo)}");
                            }
                        }
                    }

                }
                else
                {
                    // No knocks before this elimination (shouldn't happen if knocksByVictim has the victim)
                    Console.WriteLine($"   ⚠️  ELIM @ {realMatchTime:F2}s: {GetDisplayNameForPlayerId(victimId)} → Knocker recorded but no knocks before elimination");
                    creditReason = "no_knock_before_elim";
                }
            }
            // Case 2: No valid knocker, check for TEAM ELIMINATION
            // Team elim: if a teammate was knocked/eliminated before this, credit finisher for team elim
            else
            {
                var teammates = GetTeammatesOfPlayer(victimId);
                bool isTeamElim = false;

                foreach (var teammate in teammates)
                {
                    if (knocksByVictim.ContainsKey(teammate))
                    {
                        // Check if teammate has a valid knock before this elimination
                        var teammatKnocks = knocksByVictim[teammate]
                            .Where(k => k.knockTime < eventTime)
                            .OrderByDescending(k => k.knockTime)
                            .ToList();

                        if (teammatKnocks.Count > 0)
                        {
                            // Teammate was knocked, check if they were finished before this victim
                            var (_, knockTime) = teammatKnocks.First();

                            // Check if teammate was eliminated between their knock and this time
                            var teammateElimBetweenKnockAndNow = uniqueKillFeed
                                .Where(e => e.PlayerName == teammate &&
                                           e.IsDowned != true &&  // elimination
                                           (e.ReplicatedWorldTimeSecondsDouble ?? 0) > knockTime &&
                                           (e.ReplicatedWorldTimeSecondsDouble ?? 0) < eventTime)
                                .Any();

                            if (teammateElimBetweenKnockAndNow)
                            {
                                isTeamElim = true;
                                Console.WriteLine($"   🤝 TEAM ELIM DETECTED: Teammate {GetDisplayNameForPlayerId(teammate)} was knocked and eliminated before this");
                                break;
                            }
                        }
                    }
                }

                if (isTeamElim && finisherId != null && timelines.ContainsKey(finisherId))
                {
                    var finisherStatus = timelines[finisherId].GetStatusAt(eventTime);

                    if (finisherStatus == PlayerStatus.Dead)
                    {
                        Console.WriteLine($"   ❌ TEAM ELIM REJECTED: Finisher {GetDisplayNameForPlayerId(finisherId)} was DEAD at {eventTime:F2}s - cannot credit dead player");
                        creditReason = "team_elim_finisher_dead";
                    }
                    else if (finisherStatus == PlayerStatus.Alive)
                    {
                        creditTo = finisherId;
                        creditReason = "team_elim";
                        Console.WriteLine($"   ✅ ELIM @ {realMatchTime:F2}s: {GetDisplayNameForPlayerId(victimId)} → Credit to finisher {GetDisplayNameForPlayerId(creditTo)} (TEAM ELIMINATION)");
                    }
                    else
                    {
                        Console.WriteLine($"   ❌ ELIM @ {realMatchTime:F2}s: {GetDisplayNameForPlayerId(victimId)} → Team elim but finisher not alive");
                        creditReason = "team_elim_finisher_knocked";
                    }
                }
                else if (finisherId != null && timelines.ContainsKey(finisherId))
                {
                    // Not a team elim, check if finisher is valid
                    var finisherStatus = timelines[finisherId].GetStatusAt(eventTime);

                    if (finisherStatus == PlayerStatus.Dead)
                    {
                        Console.WriteLine($"   ❌ FINISHER DEAD: {GetDisplayNameForPlayerId(finisherId)} was DEAD at {eventTime:F2}s - cannot credit dead player");
                        creditReason = "finisher_dead";
                    }
                    else if (finisherStatus == PlayerStatus.Alive)
                    {
                        creditTo = finisherId;
                        creditReason = "finisher";
                        Console.WriteLine($"   ✅ ELIM @ {realMatchTime:F2}s: {GetDisplayNameForPlayerId(victimId)} → Credit to finisher {GetDisplayNameForPlayerId(creditTo)}");
                    }
                    else
                    {
                        Console.WriteLine($"   ❌ ELIM @ {realMatchTime:F2}s: {GetDisplayNameForPlayerId(victimId)} → Finisher not alive at elimination time");
                        creditReason = "finisher_knocked";
                    }
                }
                else
                {
                    Console.WriteLine($"   ❌ ELIM @ {realMatchTime:F2}s: {GetDisplayNameForPlayerId(victimId)} → No valid knocker, team elim, or finisher");
                    creditReason = "no_valid_credit";
                }
            }

            // Award credit if determined
            if (creditTo != null)
            {
                if (!_eliminationCounts.ContainsKey(creditTo))
                    _eliminationCounts[creditTo] = 0;
                _eliminationCounts[creditTo]++;
                totalCreditsAwarded++;
            }

            // Mark as processed
            processedEliminations.Add(elimKey);
        }

        Console.WriteLine($"\n📈 FINAL RESULTS:");
        Console.WriteLine($"   Total Eliminations Processed: {totalEliminations}");
        Console.WriteLine($"   Total Credits Awarded: {totalCreditsAwarded}");

        // Display awarded credits
        var matchCredits = _eliminationCounts.OrderByDescending(x => x.Value).ToList();
        foreach (var (playerId, count) in matchCredits)
        {
            var playerName = GetDisplayNameForPlayerId(playerId);
            Console.WriteLine($"   {playerName}: {count} eliminations");
        }
    }


    private bool AreAllTeammatesDead(string victimId, double eliminationTime, Dictionary<string, PlayerStateTimeline> timelines)
    {
        var victimPlayer = _players.Values.FirstOrDefault(p => p.PlayerId == victimId);
        if (victimPlayer == null || victimPlayer.TeamIndex == null)
            return false;

        var victimTeam = victimPlayer.TeamIndex;

        var teammates = _players.Values
            .Where(p => p.TeamIndex == victimTeam && p.PlayerId != victimId)
            .ToList();

        if (teammates.Count == 0)
            return false;

        // ALL teammates must be dead or knocked (not alive)
        return teammates.All(teammate =>
        {
            if (!timelines.TryGetValue(teammate.PlayerId, out var timeline))
                return true;

            var status = timeline.GetStatusAt(eliminationTime);
            return status == PlayerStatus.Dead || status == PlayerStatus.Knocked;
        });
    }

    private void InferAndInjectReboots(Dictionary<string, PlayerStateTimeline> timelines)
    {
        Console.WriteLine($"\n🔄 INFERRING REBOOTS: Detecting Dead → Knocked transitions...");

        int reboots_found = 0;

        foreach (var playerEntry in timelines)
        {
            var playerId = playerEntry.Key;
            var timeline = playerEntry.Value;
            var changes = timeline.Changes;

            var insertions = new List<(int index, StateChange newChange)>();

            // Scan through timeline looking for Dead → Knocked without Alive in between
            for (int i = 1; i < changes.Count; i++)
            {
                var prev = changes[i - 1];
                var curr = changes[i];

                // Pattern: Dead followed by Knocked = reboot occurred
                if (prev.Status == PlayerStatus.Dead && curr.Status == PlayerStatus.Knocked)
                {
                    // Insert Alive state right before the Knocked
                    // Time it just slightly before the Knocked (0.01s before)
                    var implicitAliveTime = curr.Time - 0.01;

                    var newChange = new StateChange
                    {
                        Time = implicitAliveTime,
                        Status = PlayerStatus.Alive,
                        RebootCardAvailable = false,
                        CanBeRevived = false
                    };

                    insertions.Add((i, newChange));

                    Console.WriteLine($"   🔄 REBOOT INFERRED for {GetDisplayNameForPlayerId(playerId)}:");
                    Console.WriteLine($"      Dead @ {prev.Time:F2}s → Alive @ {implicitAliveTime:F2}s (inferred) → Knocked @ {curr.Time:F2}s");

                    reboots_found++;
                }
            }

            // Insert in reverse order so indices don't shift during insertion
            foreach (var (index, newChange) in insertions.OrderByDescending(x => x.index))
            {
                changes.Insert(index, newChange);
            }
        }

        Console.WriteLine($"   Total reboots inferred: {reboots_found}");
    }

    private List<string> GetTeammatesOfPlayer(string playerId)
    {
        // Get this player's team index
        var player = _players.Values.FirstOrDefault(p => p.PlayerId == playerId);
        if (player == null)
            return new List<string>();

        int? teamIndex = player.TeamIndex;
        if (teamIndex == null || teamIndex <= 0)
            return new List<string>();

        // Find all players on the same team
        var teammates = _players.Values
            .Where(p => p.TeamIndex == teamIndex && p.PlayerId != playerId)
            .Select(p => p.PlayerId)
            .ToList();

        return teammates;
    }


    /// <summary>
    /// Build the team fully eliminated tracking during timeline building
    /// </summary>
    private void BuildTeamElimTimeline(Dictionary<string, PlayerStateTimeline> timelines)
    {
        Console.WriteLine($"\n🔄 BUILDING TEAM FULLY ELIMINATED TIMELINE:");

        // Group players by team using YOUR existing GetPlayerTeamIndex function
        var playersByTeam = new Dictionary<int, List<string>>();

        foreach (var (playerId, timeline) in timelines)
        {
            int teamIndex = GetPlayerTeamIndex(playerId);  // USE YOUR EXISTING FUNCTION

            if (teamIndex < 0) continue;  // Skip if team not found

            if (!playersByTeam.ContainsKey(teamIndex))
                playersByTeam[teamIndex] = new List<string>();

            playersByTeam[teamIndex].Add(playerId);
        }

        // For each team, find when ALL members are dead
        foreach (var (teamIndex, playerIds) in playersByTeam)
        {
            Console.WriteLine($"\n   📍 Team {teamIndex}: {playerIds.Count} players");

            // Get all death times for this team
            var allDeathTimes = new List<double>();

            foreach (var playerId in playerIds)
            {
                if (timelines.ContainsKey(playerId))
                {
                    var timeline = timelines[playerId];
                    var lastDeadEvent = timeline.Changes
                        .Where(c => c.Status == PlayerStatus.Dead)
                        .OrderByDescending(c => c.Time)
                        .FirstOrDefault();

                    if (lastDeadEvent != null)
                    {
                        allDeathTimes.Add(lastDeadEvent.Time);
                        Console.WriteLine($"      - {GetDisplayNameForPlayerId(playerId)}: died @ {lastDeadEvent.Time:F2}s");
                    }
                }
            }

            // Team is fully eliminated when the LAST member dies
            if (allDeathTimes.Count > 0)
            {
                double teamFullyElimedTime = allDeathTimes.Max();
                _teamFullyEliminatedTimes[teamIndex] = teamFullyElimedTime;
                Console.WriteLine($"      ✅ Team {teamIndex} FULLY ELIMINATED @ {teamFullyElimedTime:F2}s");
            }
        }
    }

    // IN EvaluateEliminationCredit() - ADD THIS AT THE START:
    private string EvaluateEliminationCredit(
     string victimId,
     double finishTime,
     Dictionary<string, List<(string knockerId, double knockTime)>> knockerByVictim,
     Dictionary<string, PlayerStateTimeline> timelines,
     Dictionary<string, List<double>> rebootTimeline)
    {
        if (!knockerByVictim.ContainsKey(victimId)) //handle self elims
            return null;

        var knockList = knockerByVictim[victimId]; //get list of knocks for victim
        var validKnocks = knockList.Where(k => k.knockTime < finishTime).OrderByDescending(k => k.knockTime).ToList(); //filter knocks before finish time

        if (validKnocks.Count == 0)
            return null; //no valid knocks return no credit

        var (knockerId, knockTime) = validKnocks.First();

        if (!timelines.ContainsKey(knockerId) || !timelines.ContainsKey(victimId))
            return null; //no timelines for knocker or victim return no credit

        bool isDebug = true; //set to true for detailed debug output

        if (isDebug)
        {
            Console.WriteLine($"\n        [DETAILED DEBUG] Evaluating {GetDisplayNameForPlayerId(victimId)} elimination @ {finishTime:F2}s");
            Console.WriteLine($"        [DETAILED DEBUG] Knocker: {GetDisplayNameForPlayerId(knockerId)} knocked @ {knockTime:F2}s");
        }

        // 🔧 CRITICAL NEW RULE: Check if knocker's team was fully eliminated before victim died
        int knockerTeamIndex = GetPlayerTeamIndex(knockerId);  //get knocker's team index

        if (isDebug)
            Console.WriteLine($"        [DETAILED DEBUG] knockerTeamIndex={knockerTeamIndex}");

        if (knockerTeamIndex >= 0 && _teamFullyEliminatedTimes.ContainsKey(knockerTeamIndex))
        {
            double teamElimTime = _teamFullyEliminatedTimes[knockerTeamIndex];

            if (isDebug)
                Console.WriteLine($"        [DETAILED DEBUG] Team fully eliminated @ {teamElimTime:F2}s, victim finish @ {finishTime:F2}s");

            // 🔧 CRITICAL: Only reject if team died STRICTLY BEFORE finish
            // If team dies at SAME TIME as finish, knocker still gets credit
            if (teamElimTime < finishTime)
            {
                Console.WriteLine($"        ❌ CRITICAL RULE FAILED: Knocker's team fully eliminated @ {teamElimTime:F2}s");
                Console.WriteLine($"           Victim didn't die until {finishTime:F2}s");
                Console.WriteLine($"           Per Fortnite mechanics: Team elimination breaks knock attribution");
                return null;
            }
            else if (teamElimTime == finishTime)
            {
                Console.WriteLine($"        ℹ️  Team died at SAME TIME as finish @ {teamElimTime:F2}s - processing as simultaneous event");
            }
        }

        var knockerTimeline = timelines[knockerId];
        var victimTimeline = timelines[victimId];

        // Rule 1: Knocker must have been alive at knock time
        var knockerPlayerState = _playerStateHistory.ContainsKey(knockerId)
            ? _playerStateHistory[knockerId]
            : new List<(double timestamp, string property, object value)>();

        bool knockerWasAliveAtKnockTime = false;

        if (knockerPlayerState.Count > 0)
        {
            var stateAtKnockTime = knockerPlayerState
                .Where(s => s.timestamp <= knockTime)
                .OrderByDescending(s => s.timestamp)
                .FirstOrDefault();

            if (stateAtKnockTime.property == "bDBNO")
            {
                knockerWasAliveAtKnockTime = !(bool) stateAtKnockTime.value;
            }
            else
            {
                knockerWasAliveAtKnockTime = true;
            }

            if (isDebug)
                Console.WriteLine($"        [DETAILED DEBUG] Rule 1 - Knocker alive @ knock time? {knockerWasAliveAtKnockTime}");

            Console.WriteLine($"        [PLAYER STATE] {GetDisplayNameForPlayerId(knockerId)} @ knock time {knockTime:F2}s: {knockerWasAliveAtKnockTime}");
        }
        else
        {
            knockerWasAliveAtKnockTime = true;
            if (isDebug)
                Console.WriteLine($"        [DETAILED DEBUG] Rule 1 - No player state history, assuming alive");
        }

        if (!knockerWasAliveAtKnockTime)
        {
            Console.WriteLine($"        ❌ Rule 1 FAILED: Knocker was NOT alive at knock time {knockTime:F2}s");
            return null;
        }

        // 🔧 CRITICAL: When checking if knocker is dead at finish time, account for simultaneous team death
        // If team dies at SAME TIME as finish, check knocker status BEFORE team death (0.01s before)
        var knockerStatusAtFinish = knockerTimeline.GetStatusAt(finishTime);

        if (isDebug)
            Console.WriteLine($"        [DETAILED DEBUG] Knocker status @ {finishTime:F2}s: {knockerStatusAtFinish}");

        int knockerTeamIdx = GetPlayerTeamIndex(knockerId);
        if (knockerTeamIdx >= 0 && _teamFullyEliminatedTimes.ContainsKey(knockerTeamIdx))
        {
            double teamElimTime = _teamFullyEliminatedTimes[knockerTeamIdx];

            if (isDebug)
            {
                Console.WriteLine($"        [DETAILED DEBUG] Checking simultaneous event:");
                Console.WriteLine($"        [DETAILED DEBUG]   teamElimTime={teamElimTime:F2}s, finishTime={finishTime:F2}s, equal? {teamElimTime == finishTime}");
                Console.WriteLine($"        [DETAILED DEBUG]   knockerStatusAtFinish={knockerStatusAtFinish}, isDead? {knockerStatusAtFinish == PlayerStatus.Dead}");
            }

            if (teamElimTime == finishTime && knockerStatusAtFinish == PlayerStatus.Dead)
            {
                // Team died at same time - check if knocker was alive BEFORE team death
                var statusBefore = knockerTimeline.GetStatusAt(finishTime - 0.01);
                Console.WriteLine($"        ℹ️  SIMULTANEOUS EVENT: Checking knocker status @ {finishTime - 0.01:F2}s: {statusBefore}");
                if (isDebug)
                    Console.WriteLine($"        [DETAILED DEBUG]   Status before team death: {statusBefore}");
                knockerStatusAtFinish = statusBefore;  // ← USE the pre-death status instead
            }
        }

        if (knockerStatusAtFinish == PlayerStatus.Dead)
        {
            Console.WriteLine($"        ❌ Rule 2 FAILED: Knocker was DEAD at elimination time {finishTime:F2}s");
            Console.WriteLine($"           Knocker died before knock could be finished");
            return null;
        }

        if (isDebug)
            Console.WriteLine($"        [DETAILED DEBUG] Rule 2 PASSED - Knocker alive for {finishTime - knockTime:F2}s after knock");
        Console.WriteLine($"           Knocker remained alive for {finishTime - knockTime:F2}s after knock");


        // Rule 3: Victim must not have been revived between knock and finish
        var victimStatusAtKnock = victimTimeline.GetStatusAt(knockTime);
        var victimStatusAtFinish = victimTimeline.GetStatusAt(finishTime);

        if (isDebug)
        {
            Console.WriteLine($"        [DETAILED DEBUG] Rule 3 - Victim status:");
            Console.WriteLine($"        [DETAILED DEBUG]   @ knock {knockTime:F2}s: {victimStatusAtKnock}");
            Console.WriteLine($"        [DETAILED DEBUG]   @ finish {finishTime:F2}s: {victimStatusAtFinish}");
        }

        bool wasReallyRevived = (victimStatusAtKnock == PlayerStatus.Knocked &&
                                 victimStatusAtFinish == PlayerStatus.Alive);

        if (wasReallyRevived)
        {
            Console.WriteLine($"        ❌ Rule 3 FAILED: Victim was revived between knock and finish");
            return null;
        }

        if (isDebug)
            Console.WriteLine($"        [DETAILED DEBUG] Rule 3 PASSED - Victim not revived");

        Console.WriteLine($"        ✅ ALL RULES PASSED: Credit to {GetDisplayNameForPlayerId(knockerId)}");
        return knockerId;
    }

    private bool WasReboostedBetween(string playerId, double startTime, double endTime, Dictionary<string, List<double>> rebootTimeline)
    {
        var playerName = GetDisplayNameForPlayerId(playerId);

        Console.WriteLine($"      [REBOOT CHECK] {playerName}: checking {startTime:F2}s → {endTime:F2}s");

        if (!rebootTimeline.ContainsKey(playerId))
        {
            Console.WriteLine($"      [REBOOT CHECK] {playerName}: NOT IN REBOOT TIMELINE (total entries: {rebootTimeline.Count})");
            return false;
        }

        var allReboots = rebootTimeline[playerId];
        Console.WriteLine($"      [REBOOT CHECK] {playerName}: has {allReboots.Count} total reboots: {string.Join(", ", allReboots.Select(t => $"{t:F2}s"))}");

        var rebootsInRange = allReboots.Where(t => t > startTime && t < endTime).ToList();
        var wasRebooted = rebootsInRange.Count > 0;

        Console.WriteLine($"      [REBOOT CHECK] {playerName}: {(wasRebooted ? "✅ REBOOTED" : "❌ NOT REBOOTED")} - {rebootsInRange.Count} reboot(s) in range: {string.Join(", ", rebootsInRange.Select(t => $"{t:F2}s"))}");

        return wasRebooted;
    }
    // 🔧 Helper method to extract reboot info from player timelines
    // 🔧 Helper method to extract reboot info from player timelines
    private Dictionary<string, List<double>> BuildPlayerRebootTimeline(Dictionary<string, PlayerStateTimeline> timelines)
    {
        var rebootTimeline = new Dictionary<string, List<double>>();

        Console.WriteLine($"\n🔄 BUILDING REBOOT TIMELINE:");
        Console.WriteLine($"   Note: Using ONLY validated reboots from player state history");
        Console.WriteLine($"   (Reboots that occurred before final elimination)");

        // Instead of inferring from timelines, we need to track validated reboots
        // from the player state history during BuildPlayerTimelines()

        // For now, return empty - the real reboots are stored elsewhere
        // We'll mark which reboots are valid during BuildPlayerTimelines()

        foreach (var (playerId, timeline) in timelines)
        {
            rebootTimeline[playerId] = new List<double>();
        }

        Console.WriteLine($"\n📈 REBOOT TIMELINE SUMMARY:");
        Console.WriteLine($"   Note: Reboot validation moved to BuildPlayerTimelines()");
        Console.WriteLine($"   Only valid reboots (before final elimination) are used");

        return rebootTimeline;
    }

    // Helper to check if a player was revived or rebooted between two timestamps
    private bool WasRevivedOrRebooted(string playerId, double startTime, double endTime, Dictionary<string, List<double>> rebootTimeline)
    {
        var playerName = GetDisplayNameForPlayerId(playerId);

        Console.WriteLine($"      [REVIVAL/REBOOT CHECK] {playerName}: checking {startTime:F2}s → {endTime:F2}s");

        if (!rebootTimeline.ContainsKey(playerId))
        {
            Console.WriteLine($"      [REVIVAL/REBOOT CHECK] {playerName}: NOT IN TIMELINE (total entries: {rebootTimeline.Count})");
            return false;
        }

        var allRevivals = rebootTimeline[playerId];
        Console.WriteLine($"      [REVIVAL/REBOOT CHECK] {playerName}: has {allRevivals.Count} total revivals/reboots: {string.Join(", ", allRevivals.Select(t => $"{t:F2}s"))}");

        var revisalsInRange = allRevivals.Where(t => t > startTime && t < endTime).ToList();
        var wasRevived = revisalsInRange.Count > 0;

        Console.WriteLine($"      [REVIVAL/REBOOT CHECK] {playerName}: {(wasRevived ? "✅ REVIVED/REBOOTED" : "❌ NOT REVIVED/REBOOTED")} - {revisalsInRange.Count} revival(s) in range: {string.Join(", ", revisalsInRange.Select(t => $"{t:F2}s"))}");

        return wasRevived;
    }
    private bool IsAnyTeammateAlive(string playerId, Dictionary<string, PlayerElimState> playerState, double checkTime, HashSet<string> processedEliminations, List<KillFeedEntry> killFeed)
    {
        var teamIndex = GetPlayerTeamIndex(playerId);
        if (teamIndex < 0)
            return false;

        var teammates = playerState.Values
            .Where(p => GetPlayerTeamIndex(p.PlayerId) == teamIndex)
            .ToList();

        foreach (var teammate in teammates)
        {
            if (teammate.PlayerId.Equals(playerId, StringComparison.OrdinalIgnoreCase))
                continue;

            // Teammate is alive if:
            // 1. Status is Alive, OR
            // 2. Status is Knocked, OR
            // 3. Status is Eliminated but eliminated AFTER checkTime, OR
            // 4. Will be eliminated AT checkTime (check remaining kill feed)
            if (teammate.Status == PlayerStatus.Alive || teammate.Status == PlayerStatus.Knocked)
                return true;

            if (teammate.Status == PlayerStatus.Eliminated && teammate.EliminatedTime > checkTime)
                return true;

            // Check if teammate is being eliminated at THIS exact time (but not yet processed)
            var futureElim = killFeed.FirstOrDefault(e =>
                e.PlayerName.Equals(teammate.PlayerId, StringComparison.OrdinalIgnoreCase) &&
                Math.Abs((e.ReplicatedWorldTimeSecondsDouble ?? 0.0) - checkTime) < 0.01 &&
                !e.IsDowned == true);

            if (futureElim != null)
                continue; // They're being eliminated now too, don't count as alive
        }

        return false;
    }

    private void HandleKnockEvent(
    KillFeedEntry entry,
    string victimId,
    Dictionary<string, PlayerElimState> playerState,
    Dictionary<string, (string knockerId, int knockCount)> knockerByVictim)
    {
        string knockerId = null;
        if (entry.FinisherOrDownerActorId.HasValue)
        {
            knockerId = ResolveActorIdToPlayerId(entry.FinisherOrDownerActorId.Value);
        }

        if (string.IsNullOrEmpty(knockerId))
            return;

        var victim = playerState[victimId];
        var knocker = playerState[knockerId];

        // REVIVE DETECTION: If victim has a prior knock and wasn't eliminated, they were revived
        if (knockerByVictim.ContainsKey(victimId))
        {
            var (prevKnockerId, prevKnockCount) = knockerByVictim[victimId];

            if (victim.Status == PlayerStatus.Knocked)
            {
                Console.WriteLine($"   🔄 REVIVE DETECTED: {GetDisplayNameForPlayerId(victimId)} revived from {GetDisplayNameForPlayerId(prevKnockerId)}'s knock");
                // Clear previous knocker
                knockerByVictim.Remove(victimId);
            }
        }

        // Update victim state
        victim.Status = PlayerStatus.Knocked;
        victim.KnockedBy = knockerId;
        victim.LastKnockTime = entry.ReplicatedWorldTimeSecondsDouble ?? 0.0;

        // Store knocker info
        knockerByVictim[victimId] = (knockerId, 1);

        Console.WriteLine($"   👊 KNOCK: {GetDisplayNameForPlayerId(knockerId)} knocked {GetDisplayNameForPlayerId(victimId)}");
    }

    private void HandleEliminationEvent(
     KillFeedEntry entry,
     string victimId,
     Dictionary<string, PlayerElimState> playerState,
     Dictionary<string, (string knockerId, int knockCount)> knockerByVictim,
     HashSet<string> processedEliminations,
     Dictionary<string, int> elimCredits,
     List<KillFeedEntry> uniqueKillFeed)  // ← Add this parameter
    {
        var elimKey = $"{victimId}_{entry.ReplicatedWorldTimeSecondsDouble}";
        if (processedEliminations.Contains(elimKey))
            return;
        processedEliminations.Add(elimKey);

        var victim = playerState[victimId];
        var elimTime = entry.ReplicatedWorldTimeSecondsDouble ?? 0.0;
        bool wasRebooted = victim.Status == PlayerStatus.Eliminated;

        string creditTo = null;

        // CASE 1: Check for knocker
        if (knockerByVictim.ContainsKey(victimId))
        {
            var (knockerId, _) = knockerByVictim[victimId];
            if (IsTeamAliveAtTime(knockerId, playerState, elimTime))
            {
                creditTo = knockerId;
                Console.WriteLine($"   ⚰️  ELIMINATION: {GetDisplayNameForPlayerId(victimId)} via knocker {GetDisplayNameForPlayerId(knockerId)} (team alive)");
            }
            else
            {
                Console.WriteLine($"   ⚰️  ELIMINATION: {GetDisplayNameForPlayerId(victimId)} - knocker's team dead, NO CREDIT");
            }
            knockerByVictim.Remove(victimId);
        }
        // CASE 2: No knocker - check finisher
        else if (entry.FinisherOrDownerActorId.HasValue)
        {
            var finisherId = ResolveActorIdToPlayerId(entry.FinisherOrDownerActorId.Value);

            // ✅ DEBUG: Log the finisher details
            Console.WriteLine($"   🔍 DEBUG - Finisher data for {GetDisplayNameForPlayerId(victimId)}:");
            Console.WriteLine($"      FinisherOrDownerActorId (raw): {entry.FinisherOrDownerActorId.Value}");
            Console.WriteLine($"      Resolved FinisherId: {finisherId ?? "NULL"}");
            Console.WriteLine($"      Victim Team: {GetPlayerTeamIndex(victimId)}");

            if (finisherId != null && !finisherId.Equals(victimId, StringComparison.OrdinalIgnoreCase))
            {
                // ✅ NEW: Check if teammate is also being eliminated at this exact time
                if (IsAnyTeammateAliveConsideringCurrentBatch(victimId, playerState, elimTime, uniqueKillFeed, processedEliminations))
                {
                    Console.WriteLine($"   ⚰️  ELIMINATION: {GetDisplayNameForPlayerId(victimId)} by finisher {GetDisplayNameForPlayerId(finisherId)} - but teammates alive, NO CREDIT");
                }
                else
                {
                    creditTo = finisherId;
                    Console.WriteLine($"   ⚰️  ELIMINATION: {GetDisplayNameForPlayerId(victimId)} by finisher {GetDisplayNameForPlayerId(finisherId)} (FINAL TEAM BLOW)");
                }
            }
        }
        else
        {
            Console.WriteLine($"   ⚰️  ELIMINATION: {GetDisplayNameForPlayerId(victimId)} - environment/unknown");
        }

        victim.Status = PlayerStatus.Eliminated;
        victim.EliminatedTime = elimTime;

        if (creditTo != null)
        {
            if (!elimCredits.ContainsKey(creditTo))
                elimCredits[creditTo] = 0;
            elimCredits[creditTo]++;
        }
    }

    private bool IsAnyTeammateAliveConsideringCurrentBatch(
        string playerId,
        Dictionary<string, PlayerElimState> playerState,
        double checkTime,
        List<KillFeedEntry> uniqueKillFeed,
        HashSet<string> processedEliminations)
    {
        var teamIndex = GetPlayerTeamIndex(playerId);
        if (teamIndex < 0)
            return false;

        // Get all teammates being eliminated at this exact timestamp (not yet processed)
        var teammateDyingNow = new HashSet<string>();
        foreach (var entry in uniqueKillFeed)
        {
            if (Math.Abs((entry.ReplicatedWorldTimeSecondsDouble ?? 0.0) - checkTime) < 0.01 && !entry.IsDowned == true)
            {
                var elimKey = $"{entry.PlayerName}_{entry.ReplicatedWorldTimeSecondsDouble}";
                if (!processedEliminations.Contains(elimKey))
                {
                    if (GetPlayerTeamIndex(entry.PlayerName) == teamIndex && !entry.PlayerName.Equals(playerId, StringComparison.OrdinalIgnoreCase))
                    {
                        teammateDyingNow.Add(entry.PlayerName);
                    }
                }
            }
        }

        foreach (var teammate in playerState.Values)
        {
            if (teammate.PlayerId.Equals(playerId, StringComparison.OrdinalIgnoreCase))
                continue;

            if (GetPlayerTeamIndex(teammate.PlayerId) != teamIndex)
                continue;

            // Skip if teammate is dying right now too
            if (teammateDyingNow.Contains(teammate.PlayerId))
                continue;

            if (teammate.Status == PlayerStatus.Alive)
                return true;

            if (teammate.Status == PlayerStatus.Eliminated && teammate.EliminatedTime > checkTime)
                return true;
        }

        return false;
    }
    private int GetPlayerTeamIndex(string playerId)
    {
        if (_playerInfo != null && _playerInfo.ContainsKey(playerId))
        {
            return _playerInfo[playerId].TeamIndex;
        }

        // Fallback: try to get from builder's player data
        var playerData = GetAllPlayers().FirstOrDefault(p => p.PlayerId.Equals(playerId, StringComparison.OrdinalIgnoreCase));
        if (playerData != null)
        {
            return playerData.TeamIndex ?? -1;
        }

        return -1;
    }

    private bool IsTeamAliveAtTime(string playerId, Dictionary<string, PlayerElimState> playerState, double checkTime)
    {
        var player = playerState[playerId];
        var teamIndex = GetPlayerTeamIndex(playerId);

        if (teamIndex < 0)
            return false;

        // Find all teammates
        var teammates = playerState.Values
            .Where(p => GetPlayerTeamIndex(p.PlayerId) == teamIndex)
            .ToList();

        // Check if at least one teammate is alive at this time
        foreach (var teammate in teammates)
        {
            // Alive if: never eliminated, or eliminated after checkTime
            if (teammate.Status == PlayerStatus.Alive)
                return true;

            if (teammate.Status == PlayerStatus.Knocked)
                return true; // Knocked players count as team alive

            if (teammate.Status == PlayerStatus.Eliminated && teammate.EliminatedTime > checkTime)
                return true; // Eliminated after this check time, so was alive then
        }

        return false;
    }

    private class PlayerElimState
    {
        public string PlayerId { get; set; }
        public PlayerStatus Status { get; set; }
        public string KnockedBy { get; set; }
        public double LastKnockTime { get; set; }
        public double EliminatedTime { get; set; }
    }

    public enum PlayerStatus
    {
        Alive,
        Knocked,
        Dead,
        Eliminated,
        Rebooted
    }

    private string GetDisplayNameForPlayerId(string playerId)
    {
        if (string.IsNullOrEmpty(playerId))
            return "UNKNOWN";

        var playerData = GetAllPlayers().FirstOrDefault(p => p.PlayerId.Equals(playerId, StringComparison.OrdinalIgnoreCase));
        if (playerData != null && !string.IsNullOrEmpty(playerData.PlayerName))
            return playerData.PlayerName;

        return playerId.Substring(0, Math.Min(8, playerId.Length)) + "...";
    }

    public string ResolveActorIdToPlayerId(uint actorId)
    {
        // Method 1: Use the ActorId lookup if available
        if (_actorIdToPlayerId != null && _actorIdToPlayerId.TryGetValue(actorId, out var playerIdFromActor))
        {
            return playerIdFromActor;
        }

        // Method 2: Try TryGetPlayerDataFromActor
        if (TryGetPlayerDataFromActor(actorId, out var playerData))
        {
            return playerData.EpicId;
        }

        return null;
    }

    private string FindMostRecentKnocker(string victimId, double eliminationTime)
    {
        // Search backwards through kill feed to find most recent knock on this victim
        for (int i = KillFeed.Count - 1; i >= 0; i--)
        {
            var entry = KillFeed[i];

            // Must be a knock (IsDowned == true)
            if (entry.IsDowned != true)
            {
                continue;
            }

            // Must be the same victim
            if (entry.PlayerName != victimId)
            {
                continue;
            }

            // Must be before elimination time
            if ((entry.ReplicatedWorldTimeSecondsDouble ?? 0.0) > eliminationTime)
            {
                continue;
            }

            // Must have a knocker ActorId
            if (!entry.FinisherOrDownerActorId.HasValue || entry.FinisherOrDownerActorId.Value == 0)
            {
                continue;
            }

            // ✅ Resolve ActorId to EpicId
            if (TryGetPlayerDataFromActor(entry.FinisherOrDownerActorId.Value, out var knockerData))
            {
                return knockerData.EpicId;
            }
        }

        return null;
    }
    // <summary>
    /// Check if a player or their teammate is alive at a given timestamp
    /// </summary>
    private bool IsPlayerOrTeammateAliveAtTimestamp(string playerId, double timestamp)
    {
        var playerData = _players.Values.FirstOrDefault(p => p.PlayerId == playerId);
        if (playerData == null)
            return false;

        // If player has death time and it's before this timestamp, they're dead
        if (playerData.DeathTimeDouble.HasValue && playerData.DeathTimeDouble.Value <= timestamp)
        {
            // Check if they were rebooted after this time
            var wasRebootedAfter = _rebootedPlayers.Contains(playerId);
            if (!wasRebootedAfter)
                return false;
        }

        // Check teammates
        if (playerData.TeamIndex == null)
            return true; // Solo player

        var teammates = _players.Values
            .Where(p => p.TeamIndex == playerData.TeamIndex && p.PlayerId != playerId)
            .ToList();

        foreach (var teammate in teammates)
        {
            if (!teammate.DeathTimeDouble.HasValue || teammate.DeathTimeDouble.Value > timestamp)
            {
                return true; // At least one teammate alive
            }
        }

        return false;
    }



    /// <summary>
    /// Once a replay is fully parsed, add the data build over time to the replay.
    /// </summary>
    /// <param name="replay"></param>
    /// <returns>FortniteReplay</returns>
    public FortniteReplay Build(FortniteReplay replay)
    {
        UpdateTeamData();




        replay.GameData = GameData;
        replay.MapData = MapData;
        replay.KillFeed = KillFeed;
        replay.TeamData = _teams.Values;
        replay.PlayerData = _players.Values;
        return replay;
    }

    private bool TryGetPlayerDataFromActor(uint guid, [NotNullWhen(returnValue: true)] out PlayerData? playerData)
    {
        if (_actorToChannel.TryGetValue(guid, out var pawnChannel))
        {
            if (_pawnChannelToStateChannel.TryGetValue(pawnChannel, out var stateChannel))
            {
                return _players.TryGetValue(stateChannel, out playerData);
            }
        }
        playerData = null;
        return false;
    }



    private bool TryGetPlayerDataFromPawn(uint pawn, [NotNullWhen(returnValue: true)] out PlayerData? playerData)
    {
        if (_pawnChannelToStateChannel.TryGetValue(pawn, out var stateChannel))
        {
            return _players.TryGetValue(stateChannel, out playerData);
        }
        playerData = null;
        return false;
    }

    private void HandleQueuedPlayerPawns(uint stateChannelIndex)
    {
        if (_channelToActor.TryGetValue(stateChannelIndex, out var actorId))
        {
            if (_queuedPlayerPawns.Remove(actorId, out var playerPawns))
            {
                foreach (var playerPawn in playerPawns)
                {
                    UpdatePlayerPawn(playerPawn.ChannelId, playerPawn.PlayerPawn);
                }
            }
        }
    }
    private Dictionary<string, int> _playerUpdateCounts = new Dictionary<string, int>();
    private void PrintPlayerStateViaReflection(GameState state, double currentTime)
    {
        Console.WriteLine($"\n🔍 FULL GAME STATE @ {currentTime:F2}s:");

        var properties = typeof(GameState).GetProperties(
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.Instance);

        foreach (var prop in properties.OrderBy(p => p.Name))
        {
            try
            {
                var value = prop.GetValue(state);

                if (value == null)
                    continue;

                if (prop.PropertyType.IsGenericType && prop.PropertyType.GetGenericTypeDefinition() == typeof(List<>))
                {
                    var list = value as System.Collections.IList;
                    Console.WriteLine($"      {prop.Name}: List with {list?.Count ?? 0} items");
                }
                else
                {
                    Console.WriteLine($"      {prop.Name}: {value}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"      {prop.Name}: ERROR - {ex.Message}");
            }
        }
    }

    public void UpdateGameState(GameState state)
    {

        var currentTime = ReplicatedWorldTimeSecondsDouble ?? 0;

        Console.WriteLine($"[C#_GAMESTATE] UpdateGameState called at time={currentTime:F2}s");
        Console.WriteLine($"[C#_GAMESTATE]   GameSessionId: {state?.GameSessionId}");
        Console.WriteLine($"[C#_GAMESTATE]   bReplicatedHasBegunPlay: {state?.bReplicatedHasBegunPlay}");
        Console.WriteLine($"[C#_GAMESTATE]   ReplicatedWorldTimeSeconds: {state?.ReplicatedWorldTimeSeconds}");
        Console.WriteLine($"[C#_GAMESTATE]   ReplicatedWorldTimeSecondsDouble: {state?.ReplicatedWorldTimeSecondsDouble}");

        OnGameStateUpdate(state);
        PrintPlayerStateViaReflection(state, currentTime);

        GameData.GameSessionId ??= state?.GameSessionId;
        GameData.UtcTimeStartedMatch ??= state.UtcTimeStartedMatch?.Time;
        GameData.MatchEndTime ??= state.EndGameStartTime;
        GameData.MapInfo ??= state.MapInfo?.Name;

        GameData.IsLargeTeamGame ??= state.bIsLargeTeamGame;
        GameData.TournamentRound ??= state.EventTournamentRound;

        GameData.AdditionalPlaylistLevels ??= state.AdditionalPlaylistLevelsStreamed?.Select(i => i.Name);

        GameData.MaxPlayers ??= state.TeamCount;
        GameData.TeamSize ??= state.TeamSize;
        GameData.TeamSize ??= state.ActiveTeamNums?.Length;
        GameData.TotalBots = state.PlayerBotsLeft > GameData.TotalBots ? state.PlayerBotsLeft : GameData.TotalBots;

        GameData.TotalPlayerStructures ??= state.TotalPlayerStructures;

        GameData.AircraftStartTime ??= state.AircraftStartTime;
        GameData.SafeZonesStartTime ??= state.SafeZonesStartTime;

        MapData.BattleBusFlightPaths ??= state.TeamFlightPaths?.Select(i => new BattleBus(i) { Skin = state.DefaultBattleBus?.Name });

        if (state.ReplicatedWorldTimeSeconds != null)
        {

            ReplicatedWorldTimeSeconds = state.ReplicatedWorldTimeSeconds;
        }
        else
        {
        }

        if (state.ReplicatedWorldTimeSecondsDouble != null)
        {
            ReplicatedWorldTimeSecondsDouble = state.ReplicatedWorldTimeSecondsDouble;
        }

        // ✅ Track when aircraft starts (match begins)
        // ✅ Track when match actually begins
        if (state.bReplicatedHasBegunPlay == true && !GameData.MatchStartTime.HasValue)
        {
            GameData.MatchStartTime = state.ReplicatedWorldTimeSecondsDouble ?? 0;
            Console.WriteLine($"🎮 MATCH BEGUN at {GameData.MatchStartTime:F2}s (bReplicatedHasBegunPlay transitioned to true)");
        }

        GameData.WinningPlayerIds ??= state.WinningPlayerList;
        GameData.WinningTeam ??= state.WinningTeam;
        GameData.RecorderId ??= state.RecorderPlayerState?.Value;
    }

    public void UpdatePrivateName(uint channelIndex, PlayerNameData playerNameData)
    {
        if (_players.TryGetValue(channelIndex, out var playerData))
        {
            playerData.PlayerName = playerNameData.DecodedName;
        }
    }


    public IEnumerable<PlayerData> GetAllPlayers()
    {
        return _players.Values;
    }
    public void UpdatePlaylistInfo(PlaylistInfo playlist) => GameData.CurrentPlaylist ??= playlist.Name;

    public void UpdateGameplayModifiers(ActiveGameplayModifier modifier) => GameData.ActiveGameplayModifiers.Add(modifier.ModifierDef?.Name);

    public void UpdateTeamData()
    {
        foreach (var playerData in _players.Values)
        {
            if (playerData?.TeamIndex == null)
            {
                continue;
            }

            if (!_teams.TryGetValue(playerData.TeamIndex, out var teamData))
            {
                _teams[playerData.TeamIndex] = new TeamData()
                {
                    TeamIndex = playerData.TeamIndex,
                    PlayerIds = new List<int?>() { playerData.Id },
                    PlayerNames = new List<string?>() { playerData.PlayerName ?? playerData.PlayerId },
                    Placement = playerData.Placement,
                    PartyOwnerId = playerData.IsPartyLeader ? playerData.Id : null,
                    TeamKills = playerData.TeamKills
                };
                continue;
            }

            teamData.Placement ??= playerData.Placement;
            teamData.TeamKills ??= playerData.TeamKills;

            teamData.PlayerIds.Add(playerData.Id);
            teamData.PlayerNames.Add(playerData.PlayerName ?? playerData.PlayerId);
            if (playerData.IsPartyLeader)
            {
                teamData.PartyOwnerId = playerData.Id;
            }
        }
    }

    private Dictionary<uint, int> _lastKnownKillScore = new Dictionary<uint, int>();
    private Dictionary<uint, List<(int killScore, double timestamp)>> _killScoreHistory = new Dictionary<uint, List<(int, double)>>();

    private Dictionary<uint, int> _lastKnownAthenaKills = new Dictionary<uint, int>();
    private Dictionary<uint, List<(int athenaKills, double timestamp)>> _athenaKillsHistory = new Dictionary<uint, List<(int, double)>>();

    public Dictionary<string, int> GetFinalKillScores()
    {
        var result = new Dictionary<string, int>();

        foreach (var kvp in _lastKnownKillScore)
        {
            var playerId = GetPlayerIdFromChannel(kvp.Key);
            if (!string.IsNullOrEmpty(playerId))
            {
                result[playerId] = kvp.Value;
            }
        }

        return result;
    }


    public Dictionary<string, int> GetEliminationCounts()
    {
        return _eliminationCredits;
    }

    public Dictionary<string, int> GetFinalAthenaKills()
    {
        var result = new Dictionary<string, int>();

        foreach (var kvp in _lastKnownAthenaKills)
        {
            var playerId = GetPlayerIdFromChannel(kvp.Key);
            if (!string.IsNullOrEmpty(playerId))
            {
                result[playerId] = kvp.Value;
            }
        }

        return result;
    }

    public void PrintFinalAthenaKills()
    {
        Console.WriteLine($"\n╔════════════════════════════════════════════╗");
        Console.WriteLine($"║ 🎯 FINAL ATHENAKILLS FROM REPLAY           ║");
        Console.WriteLine($"╚════════════════════════════════════════════╝\n");

        var finalScores = GetFinalAthenaKills();

        if (finalScores.Count == 0)
        {
            Console.WriteLine("No AthenaKills data captured");
            return;
        }

        Console.WriteLine($"Total players with AthenaKills: {finalScores.Count}\n");

        foreach (var kvp in finalScores.OrderByDescending(x => x.Value))
        {
            var playerName = GetDisplayNameForPlayerId(kvp.Key);
            Console.WriteLine($"  {playerName,-40} : {kvp.Value} kills");
        }

        Console.WriteLine($"\n{new string('─', 60)}");
        Console.WriteLine($"Total eliminations recorded: {finalScores.Values.Sum()}");
    }


    public int GetEliminationCountWithAthenaKillsPrimary(string playerId, Dictionary<string, int> finalAthenaKills)
    {
        var finalKillScores = GetFinalKillScores();

        if (finalAthenaKills.ContainsKey(playerId))
        {
            return finalAthenaKills[playerId];
        }
        else if (finalKillScores.ContainsKey(playerId))
        {
            return finalKillScores[playerId];
        }

        return 0;
    }



    public void PrintFinalKillScores()
    {
        Console.WriteLine($"\n╔════════════════════════════════════════════╗");
        Console.WriteLine($"║ 🏆 FINAL KILLSCORES FROM REPLAY            ║");
        Console.WriteLine($"╚════════════════════════════════════════════╝\n");

        var finalScores = GetFinalKillScores();

        if (finalScores.Count == 0)
        {
            Console.WriteLine("No KillScore data captured");
            return;
        }

        Console.WriteLine($"Total players with KillScore: {finalScores.Count}\n");

        foreach (var kvp in finalScores.OrderByDescending(x => x.Value))
        {
            var playerName = GetDisplayNameForPlayerId(kvp.Key);
            Console.WriteLine($"  {playerName,-40} : {kvp.Value} kills");
        }

        Console.WriteLine($"\n{new string('─', 60)}");
        Console.WriteLine($"Total eliminations recorded: {finalScores.Values.Sum()}");
    }

    private Dictionary<uint, FortPlayerState> _lastPlayerStates = new();
    public Dictionary<short, string> persistentIdToPlayerId = new(); // WorldPlayerId -> Internal PlayerId
    public void UpdatePlayerState(uint channelIndex, FortPlayerState state)
    {
        var playerId = GetPlayerIdFromChannel(channelIndex);

        if (!string.IsNullOrEmpty(playerId) && !_channelToPlayerId.ContainsKey(channelIndex))
        {
            _channelToPlayerId[channelIndex] = playerId;
            Console.WriteLine($"✅ Mapped Channel {channelIndex} → PlayerId {playerId}");
        }

        if (playerId == null)
        {
            Console.WriteLine("PlayerID is null");
        }

        if (state != null)
        {
            // ✅ ALWAYS capture WorldPlayerId → UniqueID mapping throughout the replay
            if (state.WorldPlayerId.HasValue)
            {
                short worldPlayerId = state.WorldPlayerId.Value;

                string epicId = null;
                if (!string.IsNullOrEmpty(playerId))
                {
                    epicId = playerId;
                }
                else if (!string.IsNullOrEmpty(state.UniqueID))
                {
                    epicId = state.UniqueID;
                }
                else if (!string.IsNullOrEmpty(state.UniqueId))
                {
                    epicId = state.UniqueId;
                }
                else if (!string.IsNullOrEmpty(state.PlatformUniqueNetId))
                {
                    epicId = state.PlatformUniqueNetId;
                }

                if (!string.IsNullOrEmpty(epicId))
                {
                    if (epicId.Contains("["))
                        epicId = epicId.Split('[')[0].Trim();

                    persistentIdToPlayerId[worldPlayerId] = epicId;
                    Console.WriteLine($"✅ [UpdatePlayerState] WorldPlayerId {worldPlayerId} → {epicId}");
                }
            }

            // ✅ Get player name for logging
            string playerName = null;
            if (state.PlayerNamePrivate != null)
                playerName = state.PlayerNamePrivate;
            else if (state.PlayerNameCustomOverride?.Text != null)
                playerName = state.PlayerNameCustomOverride.Text;
            else if (!string.IsNullOrEmpty(state.UniqueId))
                playerName = state.UniqueId;
            else if (!string.IsNullOrEmpty(state.UniqueID))
                playerName = state.UniqueID;
            else
                playerName = playerId;

            var timestamp = ReplicatedWorldTimeSecondsDouble ?? 0;
            var props = state.GetType().GetProperties();

            var nonNullProps = new List<string>();

            foreach (var prop in props)
            {
                var value = prop.GetValue(state);
                if (value != null)
                {
                    nonNullProps.Add($"{prop.Name}: {value}");
                }
            }

            if (nonNullProps.Any())
            {
                Console.WriteLine($"\n📋[FORTPLAYRSTATE_DBG] {playerName} @ {timestamp:F2}s:");
                foreach (var prop in nonNullProps)
                {
                    Console.WriteLine($"   {prop}");
                }
            }
        }
        if (state != null)
        {
            if (!string.IsNullOrEmpty(playerId))
            {
                if (!_playerStateHistory.ContainsKey(playerId))
                    _playerStateHistory[playerId] = new List<(double, string, object)>();

                var currentTime = ReplicatedWorldTimeSecondsDouble ?? 0;
                var history = _playerStateHistory[playerId];

                TrackStateChange(history, currentTime, "bDBNO", state.bDBNO);
                TrackStateChange(history, currentTime, "RebootCounter", state.RebootCounter);
                TrackStateChange(history, currentTime, "bResurrectionChipAvailable", state.bResurrectionChipAvailable);
                TrackStateChange(history, currentTime, "bResurrectingNow", state.bResurrectingNow);
                TrackStateChange(history, currentTime, "bActiveBeingRebooted", state.bActiveBeingRebooted);
            }
            else
            {
                Console.WriteLine("Tracking state history player id resolvation is null");
            }

            if (state.AthenaKills.HasValue)
            {
                int currentAthenaKills = (int) state.AthenaKills.Value;

                if (!_lastKnownAthenaKills.TryGetValue(channelIndex, out int lastAthenaKills))
                {
                    lastAthenaKills = 0;
                }

                if (currentAthenaKills != lastAthenaKills)
                {
                    _lastKnownAthenaKills[channelIndex] = currentAthenaKills;

                    if (!_athenaKillsHistory.ContainsKey(channelIndex))
                        _athenaKillsHistory[channelIndex] = new List<(int, double)>();

                    _athenaKillsHistory[channelIndex].Add((currentAthenaKills, ReplicatedWorldTimeSecondsDouble ?? 0.0));
                }
            }

            if (state.KillScore.HasValue)
            {
                int currentKillScore = (int) state.KillScore.Value;

                if (!_lastKnownKillScore.TryGetValue(channelIndex, out int lastKillScore))
                {
                    lastKillScore = 0;
                }

                if (currentKillScore != lastKillScore)
                {
                    _lastKnownKillScore[channelIndex] = currentKillScore;

                    if (!_killScoreHistory.ContainsKey(channelIndex))
                        _killScoreHistory[channelIndex] = new List<(int, double)>();

                    _killScoreHistory[channelIndex].Add((currentKillScore, ReplicatedWorldTimeSecondsDouble ?? 0.0));
                }
            }

            if (state.bOnlySpectator == true)
            {
                _onlySpectatingPlayers.Add(channelIndex);
                return;
            }

            if (_onlySpectatingPlayers.Contains(channelIndex))
            {
                return;
            }

            var isNewPlayer = !_players.TryGetValue(channelIndex, out var playerData);

            if (isNewPlayer)
            {
                playerData = new PlayerData(state);

                if (_channelToActor.TryGetValue(channelIndex, out var actorId) && actorId == GameData.RecorderId)
                {
                    playerData.IsReplayOwner = true;
                }

                _players[channelIndex] = playerData;

                // ✅ Track spawn time using available player info
                string spawnPlayerId = playerId;
                if (string.IsNullOrEmpty(spawnPlayerId) && !string.IsNullOrEmpty(state.UniqueID))
                {
                    spawnPlayerId = state.UniqueID;
                    if (spawnPlayerId.Contains("["))
                        spawnPlayerId = spawnPlayerId.Split('[')[0].Trim();
                }

                if (!string.IsNullOrEmpty(spawnPlayerId))
                {
                    playerSpawnTimes[spawnPlayerId] = ReplicatedWorldTimeSecondsDouble ?? 0;
                    Console.WriteLine($"[PLAYER_SPAWN] {spawnPlayerId} spawned at {playerSpawnTimes[spawnPlayerId]:F2}s");
                }
                else
                {
                    Console.WriteLine($"[PLAYER_SPAWN_DEBUG] No playerId for channel {channelIndex}");
                }
            }
            if (state.RebootCounter > 0 && state.RebootCounter > playerData.RebootCounter)
            {
                playerData.RebootCounter = state.RebootCounter;

                // ✅ Add this - track rebooted players
                if (!string.IsNullOrEmpty(playerId))
                {
                    _rebootedPlayers.Add(playerId);
                    Console.WriteLine($"[REBOOT_TRACKED] {playerId} was rebooted (counter: {state.RebootCounter})");
                }
            }

            if (state.RebootCounter > 0 || state.bDBNO != null || state.DeathCause != null || state.DeathLocation != null)
            {
                UpdateKillFeed(channelIndex, playerData, state);
            }

            if (state.TeamIndex > 0)
            {
                playerData.TeamIndex = state.TeamIndex;
            }

            playerData.Placement ??= state.Place;
            playerData.TeamKills = state.TeamKillScore ?? playerData.TeamKills;
            playerData.Kills = state.KillScore ?? playerData.Kills;
            playerData.HasThankedBusDriver ??= state.bThankedBusDriver;
            playerData.Disconnected ??= state.bIsDisconnected;

            playerData.DeathCause ??= state.DeathCause;
            playerData.DeathLocation ??= state.DeathLocation;
            playerData.DeathCircumstance ??= state.DeathCircumstance;
            playerData.DeathTags ??= state.DeathTags?.Tags?.Select(i => i.TagName);

            if (state.DeathTags != null)
            {
                playerData.DeathTime = ReplicatedWorldTimeSeconds;
                playerData.DeathTimeDouble = ReplicatedWorldTimeSecondsDouble;

                // ✅ Track death time
                if (!string.IsNullOrEmpty(playerId))
                {
                    playerDeathTimes[playerId] = ReplicatedWorldTimeSecondsDouble ?? 0;
                    Console.WriteLine($"[PLAYER_DEATH] {playerId} died at {playerDeathTimes[playerId]:F2}s");
                }
            }

            playerData.Cosmetics.Parts ??= state.Parts?.Name;
            playerData.Cosmetics.VariantRequiredCharacterParts ??= state.VariantRequiredCharacterParts?.Select(i => i.Name);

            if (isNewPlayer)
            {
                HandleQueuedPlayerPawns(channelIndex);
            }
        }
    }
    public Dictionary<uint, PlayerData> GetPlayers()
    {
        return _players;
    }

    private void TrackStateChange(List<(double, string, object)> history, double timestamp, string property, object value)
    {
        // Skip null values
        if (value == null)
            return;

        // ✅ Skip if this exact property with this exact value already exists at this timestamp
        var lastEntry = history.LastOrDefault(h => h.Item2 == property);
        if (lastEntry.Item3 != null && Equals(lastEntry.Item3, value))
            return;

        history.Add((timestamp, property, value));
    }

    public void MapWorldPlayerIdToEpicId(short worldPlayerId, string epicId)
    {
        if (epicId.Contains("["))
            epicId = epicId.Split('[')[0].Trim();

        persistentIdToPlayerId[worldPlayerId] = epicId;
        Console.WriteLine($"✅ [Builder] WorldPlayerId {worldPlayerId} → {epicId}");
    }

    public void UpdateKillFeed(uint channelIndex, PlayerData data, FortPlayerState state)
    {



        var entry = new KillFeedEntry()
        {
            ReplicatedWorldTimeSeconds = ReplicatedWorldTimeSeconds,
            ReplicatedWorldTimeSecondsDouble = ReplicatedWorldTimeSecondsDouble,
        };

        if (state.RebootCounter != null)
        {
            entry.IsRevived = true;
        }

        if (state.bDBNO == true)
        {
            entry.IsDowned = true;
        }

        // Resolve finisher info
        if (state.FinisherOrDowner.HasValue && state.FinisherOrDowner.Value > 0)
        {
            entry.FinisherOrDownerActorId = state.FinisherOrDowner.Value;


            if (_actorToChannel.TryGetValue(state.FinisherOrDowner.Value, out var finisherChannelIndex))
            {
                //Console.WriteLine($"   ✓ Found in _actorToChannel → Channel {finisherChannelIndex}");

                if (_players.TryGetValue(finisherChannelIndex, out var finisherData))
                {
                    entry.FinisherOrDowner = finisherData.Id;  // Integer ID
                    entry.FinisherOrDownerName = finisherData.PlayerName ?? finisherData.PlayerId;  // Display name (or fallback to Epic ID)
                    entry.FinisherOrDownerIsBot = finisherData.IsBot;

                    //Console.WriteLine($"   ✓ Found in _players → ID: {finisherData.Id}");
                    //Console.WriteLine($"   ✓ Name: {entry.FinisherOrDownerName}");
                    //Console.WriteLine($"   ✓ IsBot: {finisherData.IsBot}");
                }
                else
                {
                    //Console.WriteLine($"   ✗ Channel {finisherChannelIndex} NOT in _players!");
                    //Console.WriteLine($"      Available player channels: {string.Join(", ", _players.Keys.Take(20))}");
                }
            }
            else
            {
                //Console.WriteLine($"   ✗ Actor ID {state.FinisherOrDowner.Value} NOT in _actorToChannel!");
                //Console.WriteLine($"      Total actors in mapping: {_actorToChannel.Count}");
                //Console.WriteLine($"      Known actor IDs: {string.Join(", ", _actorToChannel.Keys.OrderBy(x => x).Take(50))}");

                // Additional debugging: check if this actor ID is close to any known actors
                var closeActors = _actorToChannel.Keys.Where(k => Math.Abs((int) k - (int) state.FinisherOrDowner.Value) <= 100).ToList();
                if (closeActors.Any())
                {
                    //Console.WriteLine($"      Nearby actors (±100): {string.Join(", ", closeActors.OrderBy(x => x))}");
                }
            }
        }
        entry.PlayerId = data.Id;
        entry.PlayerName = data.PlayerId;  // Epic ID
        entry.PlayerIsBot = data.IsBot;

        entry.Distance ??= state.Distance;
        entry.DeathCause ??= state.DeathCause;
        entry.DeathLocation ??= state.DeathLocation;
        entry.DeathCircumstance ??= state.DeathCircumstance;
        entry.DeathTags ??= state.DeathTags?.Tags?.Select(i => i.TagName);

        KillFeed.Add(entry);
    }

    /// <summary>
    /// After external data has been read and player names
    /// update kill feed entries with the actual display names
    /// </summary>
    public void UpdateKillFeedWithNames()
    {
        foreach (var entry in KillFeed)
        {
            // Update finisher display name if we have the actor ID
            if (entry.FinisherOrDownerActorId.HasValue && entry.FinisherOrDownerActorId.Value > 0)
            {
                if (_actorToChannel.TryGetValue(entry.FinisherOrDownerActorId.Value, out var finisherChannelIndex))
                {
                    if (_players.TryGetValue(finisherChannelIndex, out var finisherData))
                    {
                        entry.FinisherOrDownerName = finisherData.PlayerName ?? finisherData.PlayerId;
                    }
                }
            }
        }
    }


    public void UpdatePlayerPawn(uint channelIndex, PlayerPawn pawn)
    {
        PlayerData playerState;

        if (pawn.PlayerState.HasValue)
        {
            // Update _pawnChannelToStateChannel everytime we receive a PlayerState value for a given channel
            var actorId = pawn.PlayerState.Value;
            if (_actorToChannel.TryGetValue(actorId, out var stateChannelIndex))
            {
                _pawnChannelToStateChannel[channelIndex] = stateChannelIndex;
            }
            else
            {
                if (!_queuedPlayerPawns.TryGetValue(actorId, out var playerPawns))
                {
                    playerPawns = new List<QueuedPlayerPawn>();
                    _queuedPlayerPawns[actorId] = playerPawns;
                }

                playerPawns.Add(new QueuedPlayerPawn
                {
                    ChannelId = channelIndex,
                    PlayerPawn = pawn
                });

                return;
            }

            playerState = _players[stateChannelIndex];
        }
        else
        {
            if (!TryGetPlayerDataFromPawn(channelIndex, out playerState))
            {
                return;
            }
        }

        // LOG BUILDINGSTATE
        if (pawn.BuildingState != null)
        {
            var currentTime = (float) (ReplicatedWorldTimeSecondsDouble ?? 0.0);

            // If it's an object with properties, print them all
            var buildingStateType = pawn.BuildingState.GetType();
            if (!buildingStateType.IsPrimitive && buildingStateType != typeof(string))
            {
                foreach (var prop in buildingStateType.GetProperties())
                {
                    try
                    {
                        var value = prop.GetValue(pawn.BuildingState);
                        Console.WriteLine($"   {prop.Name} = {value}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"   {prop.Name} = <error: {ex.Message}>");
                    }
                }
            }
        }
        else
        {
        }

        playerState.Cosmetics.Character ??= pawn.Character?.Name;
        playerState.Cosmetics.BannerColorId ??= pawn.BannerColorId;
        playerState.Cosmetics.BannerIconId ??= pawn.BannerIconId;
        playerState.Cosmetics.IsDefaultCharacter ??= pawn.bIsDefaultCharacter;
        playerState.Cosmetics.Backpack ??= pawn.Backpack?.Name;
        playerState.Cosmetics.PetSkin ??= pawn.PetSkin?.Name;
        playerState.Cosmetics.Glider ??= pawn.Glider?.Name;
        playerState.Cosmetics.LoadingScreen ??= pawn.LoadingScreen?.Name;
        playerState.Cosmetics.MusicPack ??= pawn.MusicPack?.Name;
        playerState.Cosmetics.Pickaxe ??= pawn.Pickaxe?.Name;
        playerState.Cosmetics.SkyDiveContrail ??= pawn.SkyDiveContrail?.Name;
        playerState.Cosmetics.Dances ??= pawn.Dances?.Select(i => i.Name);
        playerState.Cosmetics.ItemWraps ??= pawn.ItemWraps?.Select(i => i.Name);

        if (pawn.CurrentWeapon != null)
        {
            playerState.CurrentWeapon = pawn.CurrentWeapon;
        }

        if (pawn.ReplicatedMovement != null)
        {
            var newLocation = new PlayerMovement
            {
                ReplicatedMovement = pawn.ReplicatedMovement,
                ReplicatedWorldTimeSeconds = ReplicatedWorldTimeSeconds,
                ReplicatedWorldTimeSecondsDouble = ReplicatedWorldTimeSecondsDouble,
                LastUpdateTime = pawn.ReplayLastTransformUpdateTimeStamp,
                bIsCrouched = pawn.bIsCrouched,
                bIsInAnyStorm = pawn.bIsInAnyStorm,
                bIsZiplining = pawn.bIsZiplining,
                bIsTargeting = pawn.bIsTargeting,
                bIsHonking = pawn.bIsHonking,
                bIsJumping = pawn.bIsJumping,
                bIsPlayingEmote = pawn.bIsPlayingEmote,
                bIsSprinting = pawn.bIsSprinting,
                bIsWaitingForEmoteInteraction = pawn.bIsWaitingForEmoteInteraction,
                bIsSlopeSliding = pawn.bIsSlopeSliding,
                bIsSkydiving = pawn.bIsSkydiving,
                bIsSkydivingFromLaunchPad = pawn.bIsSkydivingFromLaunchPad,
                bIsSkydivingFromBus = pawn.bIsSkydivingFromBus,
                bIsParachuteOpen = pawn.bIsParachuteOpen,
                bIsParachuteForcedOpen = pawn.bIsParachuteForcedOpen,
                bIsDBNO = pawn.bIsDBNO,
                bIsInWaterVolume = pawn.bIsInWaterVolume,
            };
            playerState.Locations.Add(newLocation);
        }
    }
    public void UpdateInventory(uint channelIndex, FortInventory fortInventory)
    {
        if (!_inventories.TryGetValue(channelIndex, out var inventory))
        {
            // TODO updates for unknown parent inventory !?
            // TODO receive inventory for some random channel without replaypawn...?
            if (!fortInventory.ReplayPawn.HasValue)
            {
                return;
            }

            inventory = new Inventory()
            {
                Id = channelIndex,
                ReplayPawn = fortInventory.ReplayPawn
            };
            _inventories[channelIndex] = inventory;
        }

        if (fortInventory.ReplayPawn > 0)
        {
            inventory.ReplayPawn = fortInventory.ReplayPawn;
        }

        if (!inventory.PlayerId.HasValue)
        {
            if (TryGetPlayerDataFromActor(inventory.ReplayPawn.GetValueOrDefault(), out var playerData))
            {
                inventory.PlayerId = playerData.Id;
                inventory.PlayerName = playerData.PlayerId;
                playerData.InventoryId = inventory.Id;
            }
        }

        // if (!fortInventory.A.HasValue)
        //  {
        //     return;
        //  }

        //var inventoryItem = new InventoryItem()
        //{
        //  Count = fortInventory.Count,
        // ItemDefinition = fortInventory.ItemDefinition?.Name,
        // OrderIndex = fortInventory.OrderIndex,
        // Durability = fortInventory.Durability,
        // Level = fortInventory.Level,
        //  LoadedAmmo = fortInventory.LoadedAmmo,
        // A = fortInventory.A,
        //  B = fortInventory.B,
        //  C = fortInventory.C,
        //  D = fortInventory.D
        // };
        // inventory.Items.Add(inventoryItem);
    }

    public void UpdateWeapon(uint channelIndex, BaseWeapon weapon)
    {
        if (!_weapons.TryGetValue(channelIndex, out var newWeapon))
        {
            if (!_unknownWeapons.TryGetValue(channelIndex, out newWeapon))
            {
                newWeapon = new WeaponData();
                _weapons[channelIndex] = newWeapon;
            }
            else
            {
                _unknownWeapons.Remove(channelIndex);
            }
        }

        newWeapon.bIsEquippingWeapon ??= weapon.bIsEquippingWeapon;
        newWeapon.bIsReloadingWeapon ??= weapon.bIsReloadingWeapon;
        newWeapon.WeaponLevel ??= weapon.WeaponLevel;
        newWeapon.AmmoCount ??= weapon.AmmoCount;
        newWeapon.LastFireTimeVerified ??= weapon.LastFireTimeVerified;
        newWeapon.A ??= weapon.A;
        newWeapon.B ??= weapon.B;
        newWeapon.C ??= weapon.C;
        newWeapon.D ??= weapon.D;
        newWeapon.WeaponName ??= weapon.WeaponData?.Name;
    }

    public void UpdateSafeZones(SafeZoneIndicator safeZone)
    {
        TrackZoneSurvival(safeZone);

        // ✅ Debug via reflection
        Console.WriteLine($"\n[SAFEZONE_DEBUG] SafeZoneIndicator properties:");
        var props = safeZone.GetType().GetProperties();
        foreach (var prop in props)
        {
            try
            {
                var value = prop.GetValue(safeZone);
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

        if (safeZone.SafeZoneStartShrinkTime <= 0 && safeZone.SafeZoneFinishShrinkTime <= 0)
        {
            return;
        }

        MapData.SafeZones.Add(new SafeZone(safeZone));
    }



    public void UpdateLlama(uint channelIndex, SupplyDropLlama supplyDropLlama)
    {
        if (!_llamas.TryGetValue(channelIndex, out var llama))
        {
            llama = new Llama(channelIndex, supplyDropLlama);
            MapData.Llamas.Add(llama);
            _llamas.Add(channelIndex, llama);
            return;
        }

        llama.LandingLocation ??= supplyDropLlama.FinalDestination;

        if (supplyDropLlama.Looted)
        {
            llama.Looted = true;
            llama.LootedTime = ReplicatedWorldTimeSeconds;
            llama.LootedTimeDouble = ReplicatedWorldTimeSecondsDouble;
        }

        if (supplyDropLlama.bHasSpawnedPickups)
        {
            llama.HasSpawnedPickups = true;
        }
    }


    public void UpdateSupplyDrop(uint channelIndex, Models.NetFieldExports.SupplyDrop supplyDrop)
    {
        if (!_drops.TryGetValue(channelIndex, out var drop))
        {
            drop = new Models.SupplyDrop(channelIndex, supplyDrop);
            MapData.SupplyDrops.Add(drop);
            _drops.Add(channelIndex, drop);
            return;
        }

        if (supplyDrop.Opened)
        {
            drop.Looted = true;
            drop.LootedTime = ReplicatedWorldTimeSeconds;
            drop.LootedTimeDouble = ReplicatedWorldTimeSecondsDouble;
        }

        if (supplyDrop.BalloonPopped)
        {
            drop.BalloonPopped = true;
            drop.BalloonPoppedTime = ReplicatedWorldTimeSeconds;
            drop.BalloonPoppedTimeDouble = ReplicatedWorldTimeSecondsDouble;

        }

        if (supplyDrop.bHasSpawnedPickups)
        {
            drop.HasSpawnedPickups = true;
        }

        if (supplyDrop.LandingLocation != null)
        {
            drop.LandingLocation = supplyDrop.LandingLocation;
        }
    }




    public void UpdateRebootVan(uint channelIndex, SpawnMachineRepData spawnMachine)
    {
        if (!_rebootVans.TryGetValue(spawnMachine.SpawnMachineRepDataHandle, out var rebootVan))
        {
            rebootVan = new RebootVan(spawnMachine);
            MapData.RebootVans.Add(rebootVan);
            _rebootVans.Add(spawnMachine.SpawnMachineRepDataHandle, rebootVan);
            return;
        }
    }

    //public void UpdateExplosion(BroadcastExplosion explosion)
    //{
    //    // ¯\_(ツ)_/¯
    //}

    public void UpdatePoiManager(FortPoiManager poiManager)
    {
        MapData.GridCountX ??= poiManager.GridCountX;
        MapData.GridCountY ??= poiManager.GridCountY;
        MapData.WorldGridStart ??= poiManager.WorldGridStart;
        MapData.WorldGridEnd ??= poiManager.WorldGridEnd;
        MapData.WorldGridSpacing ??= poiManager.WorldGridSpacing;
        MapData.WorldGridTotalSize ??= poiManager.WorldGridTotalSize;

        // ignore PoiTagContainerTable since it is just a list of all POI...
    }

    //public void UpdateGameplayCue(uint channelIndex, GameplayCue gameplayCue)
    //{
    //    // ¯\_(ツ)_/¯
    //}


    /// <summary>
    /// Get the player ID associated with a channel in Builder
    /// </summary>
    public string GetPlayerIdFromChannel(uint channelIndex)
    {
        if (_channelToPlayerId.TryGetValue(channelIndex, out var playerId))
        {
            return playerId;
        }

        // Try to get from player data
        if (_players.TryGetValue(channelIndex, out var playerData))
        {
            return playerData.PlayerId ?? playerData.Id?.ToString() ?? string.Empty;
        }

        return string.Empty;
    }

    /// <summary>
    /// Get the current replay time
    /// </summary>
    public float GetCurrentTime()
    {
        return ReplicatedWorldTimeSeconds ?? 0f;
    }

    public double GetCurrentTimeDouble()
    {
        return ReplicatedWorldTimeSecondsDouble ?? 0.0;
    }


    /// <summary>
    /// Get the previous health set for a channel (for damage tracking)
    /// </summary>
    public HealthSet GetPreviousHealthSet(uint channelIndex)
    {
        _previousHealthSets.TryGetValue(channelIndex, out var healthSet);
        return healthSet;
    }

    /// <summary>
    /// Update the health set for a channel
    /// </summary>
    public void UpdateHealthSet(uint channelIndex, HealthSet healthSet)
    {
        _previousHealthSets[channelIndex] = healthSet;
    }
}
