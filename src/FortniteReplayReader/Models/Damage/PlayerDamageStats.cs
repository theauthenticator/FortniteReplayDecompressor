using System.Collections.Generic;
using System.Linq;

namespace FortniteReplayReader.Models.Damage;

public class PlayerDamageStats
{
    public int Deaths { get; set; }
    public string PlayerId { get; set; } = string.Empty;
    public float TotalDamageDealt { get; set; }
    public float TotalDamageTaken { get; set; }
    public float TotalShieldDamageDealt { get; set; }
    public float TotalHealthDamageDealt { get; set; }
    public float TotalShieldDamageTaken { get; set; }
    public float TotalHealthDamageTaken { get; set; }
    
    public List<DamageEvent> DamageDealt { get; set; } = new();
    public List<DamageEvent> DamageTaken { get; set; } = new();
    
    public int Eliminations { get; set; }
    public int CriticalHits { get; set; }
    public int ShotsHit { get; set; }
    
    // Calculated properties
    public float AverageDamagePerHit => ShotsHit > 0 ? TotalDamageDealt / ShotsHit : 0;
    public float CriticalHitPercentage => ShotsHit > 0 ? (float)CriticalHits / ShotsHit * 100 : 0;
}