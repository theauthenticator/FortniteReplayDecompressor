using FortniteReplayReader.Models.NetFieldExports;
using FortniteReplayReader.Models.NetFieldExports.RPC;
using System.Collections.Generic;
using System.Linq;
using Unreal.Core.Models;
using System;

namespace FortniteReplayReader.Models.Damage;

public class DamageTracker
{
    public Dictionary<string, PlayerDamageStats> PlayerStats { get; set; } = new();
    private Dictionary<uint, string> ActorToPlayerId { get; set; } = new();

    // Track actor to player mapping
    public void MapActorToPlayer(uint actorId, string playerId)
    {
        ActorToPlayerId[actorId] = playerId;
    }

    // Add this method to DamageTracker.cs - UPDATED: Skip knocked/bot damage
    public void TrackHealthChange(string playerId, HealthSet oldHealth, HealthSet newHealth, float timeStamp, bool isKnockedDown = false, bool isBot = false)
    {
        if (oldHealth == null || newHealth == null) return;

        // NEW: Skip damage tracking for knocked players or bots
        if (isKnockedDown || isBot) return;

        var stats = GetOrCreatePlayerStats(playerId);

        // Calculate damage taken from health decrease
        float healthDamage = oldHealth.HealthCurrentValue - newHealth.HealthCurrentValue;
        float shieldDamage = oldHealth.ShieldCurrentValue - newHealth.ShieldCurrentValue;

        if (healthDamage > 0)
        {
            stats.DamageTaken.Add(new DamageEvent
            {
                Amount = healthDamage,
                Type = DamageType.Health,
                TimeStamp = timeStamp,
                VictimId = playerId
            });
            stats.TotalDamageTaken += healthDamage;
            stats.TotalHealthDamageTaken += healthDamage;
        }

        if (shieldDamage > 0)
        {
            stats.DamageTaken.Add(new DamageEvent
            {
                Amount = shieldDamage,
                Type = DamageType.Shield,
                TimeStamp = timeStamp,
                VictimId = playerId
            });
            stats.TotalDamageTaken += shieldDamage;
            stats.TotalShieldDamageTaken += shieldDamage;
        }
    }

    // Process damage from BatchedDamageCues - UPDATED: Skip knocked/bot damage
    public void ProcessDamageCues(string attackerId, BatchedDamageCues cues, float timeStamp, bool attackerIsBot = false, bool victimIsKnockedDown = false, bool victimIsBot = false)
    {
        if (cues?.Magnitude == null || attackerId == null) return;

        // NEW: Skip damage if attacker is bot or victim is knocked/bot
        if (attackerIsBot || victimIsKnockedDown || victimIsBot) return;

        var attackerStats = GetOrCreatePlayerStats(attackerId);
        var victimId = GetPlayerIdFromActor(cues.HitActor);

        var damageEvent = new DamageEvent
        {
            Amount = cues.Magnitude.Value,
            AttackerId = attackerId,
            VictimId = victimId,
            TimeStamp = timeStamp,
            IsCritical = cues.bIsCritical == true,
            IsFatal = cues.bIsFatal == true,
            IsShieldDamage = cues.bIsShield == true,
            Location = cues.Location,
            Type = cues.bIsShield == true ? DamageType.Shield : DamageType.Health
        };

        // Update attacker stats
        attackerStats.DamageDealt.Add(damageEvent);
        attackerStats.TotalDamageDealt += damageEvent.Amount;
        attackerStats.ShotsHit++;

        if (damageEvent.IsCritical)
            attackerStats.CriticalHits++;

        if (damageEvent.IsShieldDamage)
            attackerStats.TotalShieldDamageDealt += damageEvent.Amount;
        else
            attackerStats.TotalHealthDamageDealt += damageEvent.Amount;

        // Update victim stats if we can identify them
        if (victimId != null && !victimIsBot && !victimIsKnockedDown)
        {
            var victimStats = GetOrCreatePlayerStats(victimId);
            victimStats.DamageTaken.Add(damageEvent);
            victimStats.TotalDamageTaken += damageEvent.Amount;

            if (damageEvent.IsShieldDamage)
                victimStats.TotalShieldDamageTaken += damageEvent.Amount;
            else
                victimStats.TotalHealthDamageTaken += damageEvent.Amount;
        }
    }

    public PlayerDamageStats GetOrCreatePlayerStats(string playerId)
    {
        if (!PlayerStats.ContainsKey(playerId))
        {
            PlayerStats[playerId] = new PlayerDamageStats { PlayerId = playerId };
        }
        return PlayerStats[playerId];
    }

    // NEW: Separate method to increment shots hit (called after all filters pass)
    public void IncrementShotsHit(string playerId)
    {
        var stats = GetOrCreatePlayerStats(playerId);
        stats.ShotsHit++;
    }

    // Add this method to your existing DamageTracker class - UPDATED: Skip knocked/bot damage
    public void RecordDamage(string attacker, string victim, uint? damage, bool? isFatal, bool attackerIsBot = false, bool victimIsKnockedDown = false, bool victimIsBot = false)
    {
        if (!damage.HasValue) return;

        // NEW: Skip damage if attacker is bot or victim is knocked/bot
        if (attackerIsBot || victimIsKnockedDown || victimIsBot) return;

        var attackerStats = GetOrCreatePlayerStats(attacker);
        var victimStats = GetOrCreatePlayerStats(victim);

        var damageEvent = new DamageEvent
        {
            Amount = damage.Value,
            AttackerId = attacker,
            VictimId = victim,
            TimeStamp = 0, // You might want to pass this as a parameter
            IsFatal = isFatal == true,
            Type = DamageType.Health // Default to health damage
        };

        // Update attacker stats
        attackerStats.DamageDealt.Add(damageEvent);
        attackerStats.TotalDamageDealt += damage.Value;

        // Update victim stats
        victimStats.DamageTaken.Add(damageEvent);
        victimStats.TotalDamageTaken += damage.Value;

        // Console.WriteLine($"💥 {attacker} dealt {damage} damage to {victim}{(isFatal == true ? " (ELIMINATION)" : "")}");
    }

    public void PrintSummary()
    {
        Console.WriteLine("\n=== DAMAGE SUMMARY ===");
        Console.WriteLine("\nDamage Dealt:");
        foreach (var player in PlayerStats.Values.OrderByDescending(p => p.TotalDamageDealt))
        {
            Console.WriteLine($"{player.PlayerId}: {player.TotalDamageDealt} (Shots Hit: {player.ShotsHit})");
        }

        Console.WriteLine("\nDamage Taken:");
        foreach (var player in PlayerStats.Values.OrderByDescending(p => p.TotalDamageTaken))
        {
            Console.WriteLine($"{player.PlayerId}: {player.TotalDamageTaken}");
        }

        var summary = GetDamageSummary();
        Console.WriteLine($"\nTotal Damage Events: {PlayerStats.Values.Sum(p => p.DamageDealt.Count)}");
        Console.WriteLine($"Total Shots Hit: {summary.TotalShotsHit}");
    }

    private string? GetPlayerIdFromActor(uint? actorId)
    {
        return actorId.HasValue && ActorToPlayerId.ContainsKey(actorId.Value)
            ? ActorToPlayerId[actorId.Value]
            : null;
    }

    public DamageSummary GetDamageSummary()
    {
        return new DamageSummary
        {
            TotalDamage = PlayerStats.Values.Sum(p => p.TotalDamageDealt),
            PlayerStats = PlayerStats.Values.ToList(),
            TotalEliminations = PlayerStats.Values.Sum(p => p.Eliminations),
            TotalShotsHit = PlayerStats.Values.Sum(p => p.ShotsHit)
        };
    }
}