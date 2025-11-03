using FortniteReplayReader.Models.NetFieldExports.Vehicles;
using Unreal.Core.Attributes;
using Unreal.Core.Models;
using Unreal.Core.Models.Enums;

namespace FortniteReplayReader.Models.NetFieldExports.Builds;

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Wood/L1/PBWA_W1_RoofC.PBWA_W1_RoofC_C", minimalParseMode: ParseMode.Minimal)]
public class WoodRoof : BaseBuild
{
    [NetFieldExport("BuildingReplacementType", RepLayoutCmdType.Enum)]
    public int? BuildingReplacementType { get; set; }

    [NetFieldExport("ProxyGameplayCueDamageMagnitude", RepLayoutCmdType.Property)]
    public float? ProxyGameplayCueDamageMagnitude { get; set; }
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Wood/L1/PBWA_W1_RoofI.PBWA_W1_RoofI_C", minimalParseMode: ParseMode.Minimal)]
public class WoodRoofI : BaseBuild
{
    [NetFieldExport("BuildingReplacementType", RepLayoutCmdType.Enum)]
    public int? BuildingReplacementType { get; set; }
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Stone/L1/PBWA_S1_RoofC.PBWA_S1_RoofC_C", minimalParseMode: ParseMode.Minimal)]
public class StoneRoof : BaseBuild
{
    [NetFieldExport("BuildingReplacementType", RepLayoutCmdType.Enum)]
    public int? BuildingReplacementType { get; set; }

    [NetFieldExport("ProxyGameplayCueDamageMagnitude", RepLayoutCmdType.Property)]
    public float? ProxyGameplayCueDamageMagnitude { get; set; }

    [NetFieldExport("bUnderRepair", RepLayoutCmdType.PropertyBool)]
    public bool? bUnderRepair { get; set; }
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Metal/L1/PBWA_M1_RoofC.PBWA_M1_RoofC_C", minimalParseMode: ParseMode.Minimal)]
public class MetalRoof : BaseBuild
{
    [NetFieldExport("BuildingReplacementType", RepLayoutCmdType.Enum)]
    public int? BuildingReplacementType { get; set; }
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Stone/L1/PBWA_S1_RoofD.PBWA_S1_RoofD_C", minimalParseMode: ParseMode.Minimal)]
public class StoneRoofDome : BaseBuild
{
}