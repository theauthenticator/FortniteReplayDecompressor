using System;
using System.Collections.Generic;
using System.Linq;

namespace FortniteReplayReader.Models.Eliminations;

/// <summary>
/// Tracks eliminations based on processed kill feed logic with knock credit transfers
/// </summary>
public class ElimTracker
{
    private Dictionary<string, string> _activeKnocks = new(StringComparer.OrdinalIgnoreCase);  // victim -> knocker
    private Dictionary<string, int> _eliminationCounts = new(StringComparer.OrdinalIgnoreCase);
    private List<EliminationEventData> _allEliminationEvents = new();
    private HashSet<string> _eliminatedPlayers = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _rebootedPlayers = new(StringComparer.OrdinalIgnoreCase);

    public class EliminationEventData
    {
        public string EliminatorId { get; set; }
        public string VictimId { get; set; }
        public bool IsKnock { get; set; }
        public bool IsSelfElim { get; set; }
        public double Timestamp { get; set; }
        public string KnockCreditTo { get; set; }  // Who gets credit for the kill
        public bool WasRebooted { get; set; }
    }

    public void ProcessEliminationEvent(
        string eliminatorId, 
        string victimId, 
        bool isKnock, 
        bool isSelfElim, 
        double timestamp,
        Func<string, bool> isTeamAlive = null,
        byte weaponType = 0)
    {
        // Check for reboot detection
        if (_eliminatedPlayers.Contains(eliminatorId))
        {
            Console.WriteLine($"\n🔄 REBOOT DETECTED!");
            Console.WriteLine($"   {eliminatorId} was eliminated but is now active again");
            _eliminatedPlayers.Remove(eliminatorId);
            _rebootedPlayers.Add(eliminatorId);
        }

        if (_eliminatedPlayers.Contains(victimId) && isKnock)
        {
            Console.WriteLine($"\n🔄 REBOOT DETECTED!");
            Console.WriteLine($"   {victimId} was eliminated but is now being knocked");
            _eliminatedPlayers.Remove(victimId);
            _rebootedPlayers.Add(victimId);
        }

        string creditedPlayer = eliminatorId;

        if (isKnock)
        {
            // Check if this player was already knocked (indicates they were revived)
            if (_activeKnocks.ContainsKey(victimId))
            {
                var previousKnocker = _activeKnocks[victimId];
                Console.WriteLine($"\n🥊 KNOCK at {timestamp:F1}s:");
                Console.WriteLine($"   {eliminatorId} knocked {victimId}");
                Console.WriteLine($"   💚 REVIVE DETECTED: Was previously knocked by {previousKnocker}");
                Console.WriteLine($"   Previous knock credit cleared - new knock tracked");
            }
            else
            {
                Console.WriteLine($"\n🥊 KNOCK at {timestamp:F1}s:");
                Console.WriteLine($"   {eliminatorId} knocked {victimId}");
                Console.WriteLine($"   ⏳ Knock recorded - credit reserved for knocker");
            }

            // Record (or overwrite) the knock
            _activeKnocks[victimId] = eliminatorId;
        }
        else
        {
            // This is an elimination - check if there was a prior knock
            if (_activeKnocks.ContainsKey(victimId))
            {
                var knockerId = _activeKnocks[victimId];
                var isCreditTransfer = (eliminatorId != knockerId);

                Console.WriteLine($"\n💀 ELIMINATION at {timestamp:F1}s:");
                Console.WriteLine($"   Victim: {victimId}");

                // Check if knocker's team is alive (if validator provided)
                bool knockerTeamAlive = isTeamAlive?.Invoke(knockerId) ?? true;

                if (knockerTeamAlive)
                {
                    creditedPlayer = knockerId;

                    if (isCreditTransfer)
                    {
                        Console.WriteLine($"   Finished by: {eliminatorId}");
                        Console.WriteLine($"   ✅ CREDITED TO: {creditedPlayer} (original knocker)");
                    }
                    else
                    {
                        Console.WriteLine($"   {creditedPlayer} eliminated (finished own knock)");
                    }
                }
                else
                {
                    Console.WriteLine($"   ❌ No credit: {knockerId}'s team is dead (NOBODY gets credit)");
                    creditedPlayer = null;
                }

                _activeKnocks.Remove(victimId);
            }
            else
            {
                // Direct elimination (no prior knock)
                Console.WriteLine($"\n💀 ELIMINATION at {timestamp:F1}s:");
                Console.WriteLine($"   {eliminatorId} eliminated {victimId}");
                Console.WriteLine($"   Direct elimination (no knock phase)");
            }

            // Track elimination for reboot detection
            _eliminatedPlayers.Add(victimId);
        }

        // Store the event
        var eventData = new EliminationEventData
        {
            EliminatorId = eliminatorId,
            VictimId = victimId,
            IsKnock = isKnock,
            IsSelfElim = isSelfElim,
            Timestamp = timestamp,
            KnockCreditTo = creditedPlayer,
            WasRebooted = _rebootedPlayers.Contains(victimId)
        };

        _allEliminationEvents.Add(eventData);

        // Count only actual eliminations (not knocks, not self-elims)
        if (!isKnock && !isSelfElim && creditedPlayer != null)
        {
            if (!_eliminationCounts.ContainsKey(creditedPlayer))
            {
                _eliminationCounts[creditedPlayer] = 0;
            }

            _eliminationCounts[creditedPlayer]++;
            Console.WriteLine($"   📊 Kill #{_eliminationCounts[creditedPlayer]} for {creditedPlayer}");
        }
        else if (isSelfElim)
        {
            Console.WriteLine($"   ⏭️  SKIPPED (self-elimination)");
        }
    }

    /// <summary>
    /// Alias for ProcessEliminationEvent - records an elimination
    /// </summary>
    public void RecordElimination(
        string eliminatorId,
        string victimId,
        bool isKnock,
        bool isSelfElim,
        double timestamp,
        Func<string, bool> isTeamAlive = null,
        byte weaponType = 0)
    {
        ProcessEliminationEvent(eliminatorId, victimId, isKnock, isSelfElim, timestamp, isTeamAlive, weaponType);
    }

    public int GetEliminationCount(string playerId)
    {
        return _eliminationCounts.ContainsKey(playerId) ? _eliminationCounts[playerId] : 0;
    }

    public bool WasPlayerRebooted(string playerId)
    {
        return _rebootedPlayers.Contains(playerId);
    }

    public List<EliminationEventData> GetAllEvents()
    {
        return _allEliminationEvents;
    }

    public Dictionary<string, int> GetAllEliminationCounts()
    {
        return new Dictionary<string, int>(_eliminationCounts);
    }

    public void PrintSummary()
    {
        Console.WriteLine("\n=== ELIMINATION SUMMARY ===");
        Console.WriteLine($"Total players with eliminations: {_eliminationCounts.Count}");
        Console.WriteLine($"Total eliminations: {_eliminationCounts.Values.Sum()}");
        Console.WriteLine($"Players rebooted: {_rebootedPlayers.Count}");

        Console.WriteLine("\nElimination Counts:");
        foreach (var kvp in _eliminationCounts.OrderByDescending(x => x.Value))
        {
            var rebootStatus = _rebootedPlayers.Contains(kvp.Key) ? " [REBOOTED]" : "";
            Console.WriteLine($"  {kvp.Key}: {kvp.Value}{rebootStatus}");
        }

        Console.WriteLine("\n=== END SUMMARY ===\n");
    }
}