using Unreal.Core.Attributes;
using Unreal.Core.Contracts;
using Unreal.Core.Models;
using Unreal.Core.Models.Enums;


[NetFieldExportGroup("/Script/FortniteGame.FortSafeZoneIndicatorFuture", minimalParseMode: ParseMode.Normal)]
public class FortSafeZoneIndicatorFuture : INetFieldExportGroup
{
    [NetFieldExport("RemoteRole", RepLayoutCmdType.Ignore)]
    public object RemoteRole { get; set; }

    [NetFieldExport("Role", RepLayoutCmdType.Ignore)]
    public object Role { get; set; }

    [NetFieldExport("CurrentPhase", RepLayoutCmdType.PropertyFloat)]
    public float CurrentPhase { get; set; }

    [NetFieldExport("PhaseCount", RepLayoutCmdType.PropertyFloat)]
    public float PhaseCount { get; set; }

    [NetFieldExport("SafeZoneStartShrinkTime", RepLayoutCmdType.PropertyFloat)]
    public float SafeZoneStartShrinkTime { get; set; }

    [NetFieldExport("SafeZoneFinishShrinkTime", RepLayoutCmdType.PropertyFloat)]
    public float SafeZoneFinishShrinkTime { get; set; }

    [NetFieldExport("Radius", RepLayoutCmdType.PropertyFloat)]
    public float Radius { get; set; }

    [NetFieldExport("NextRadius", RepLayoutCmdType.PropertyFloat)]
    public float NextRadius { get; set; }

    [NetFieldExport("LastRadius", RepLayoutCmdType.PropertyFloat)]
    public float LastRadius { get; set; }

    [NetFieldExport("LastCenter", RepLayoutCmdType.PropertyVector100)]
    public FVector LastCenter { get; set; }

    [NetFieldExport("NextCenter", RepLayoutCmdType.PropertyVector100)]
    public FVector NextCenter { get; set; }
    
    [NetFieldExport("NextNextCenter", RepLayoutCmdType.Ignore)]
    public object NextNextCenter { get; set; }

    [NetFieldExport("NextNextRadius", RepLayoutCmdType.Ignore)]
    public object NextNextRadius { get; set; }
}