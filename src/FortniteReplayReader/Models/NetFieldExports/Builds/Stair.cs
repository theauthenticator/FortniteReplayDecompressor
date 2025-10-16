using FortniteReplayReader.Models.NetFieldExports.Vehicles;
using Unreal.Core.Attributes;
using Unreal.Core.Models.Enums;

namespace FortniteReplayReader.Models.NetFieldExports.Builds;

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Wood/L1/PBWA_W1_StairW.PBWA_W1_StairW_C", minimalParseMode: ParseMode.Minimal)]
public class WoodStair : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Wood/L1/PBWA_W1_StairT.PBWA_W1_StairT_C", minimalParseMode: ParseMode.Minimal)]
public class WoodStairT : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Wood/L1/PBWA_W1_StairF.PBWA_W1_StairF_C", minimalParseMode: ParseMode.Minimal)]
public class WoodStairF : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Stone/L1/PBWA_S1_StairW.PBWA_S1_StairW_C", minimalParseMode: ParseMode.Minimal)]
public class StoneStair : BaseBuild { }

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Stone/L1/PBWA_S1_StairT.PBWA_S1_StairT_C", minimalParseMode: ParseMode.Minimal)]
public class StoneStairT : BaseBuild { }

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Stone/L1/PBWA_S1_StairF.PBWA_S1_StairF_C", minimalParseMode: ParseMode.Minimal)]
public class StoneStairF : BaseBuild { }

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Metal/L1/PBWA_M1_StairW.PBWA_M1_StairW_C", minimalParseMode: ParseMode.Minimal)]
public class MetalStair : BaseBuild { }

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Metal/L1/PBWA_M1_StairT.PBWA_M1_StairT_C", minimalParseMode: ParseMode.Minimal)]
public class MetalStairT : BaseBuild { }

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Metal/L1/PBWA_M1_StairF.PBWA_M1_StairF_C", minimalParseMode: ParseMode.Minimal)]
public class MetalStairF : BaseBuild { }