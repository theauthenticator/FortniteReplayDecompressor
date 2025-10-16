using Unreal.Core.Models;
        
namespace FortniteReplayReader.Models.Damage;

public class DamageEvent
{
    public float Amount { get; set; }
    public DamageType Type { get; set; }
    public float TimeStamp { get; set; }
    public string? VictimId { get; set; }
    public string? AttackerId { get; set; }
    public string? WeaponType { get; set; }
    public bool IsCritical { get; set; }
    public bool IsFatal { get; set; }
    public bool IsShieldDamage { get; set; }
    public FVector Location { get; set; }
    public double? Distance { get; set; }
}

public enum DamageType
{
    Health,
    Shield,
    Combined
}