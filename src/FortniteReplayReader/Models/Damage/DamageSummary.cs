using System.Collections.Generic;
using System.Linq;

namespace FortniteReplayReader.Models.Damage;

public class DamageSummary
{
    public float TotalDamage { get; set; }
    public List<PlayerDamageStats> PlayerStats { get; set; } = new();
    public int TotalEliminations { get; set; }
    public int TotalShotsHit { get; set; }
    
    // Top performers
    public PlayerDamageStats? HighestDamagePlayer => PlayerStats.OrderByDescending(p => p.TotalDamageDealt).FirstOrDefault();
    public PlayerDamageStats? MostEliminationsPlayer => PlayerStats.OrderByDescending(p => p.Eliminations).FirstOrDefault();
    public PlayerDamageStats? HighestAccuracyPlayer => PlayerStats.OrderByDescending(p => p.CriticalHitPercentage).FirstOrDefault();
}