using FortniteReplayReader.Models.NetFieldExports.Vehicles;
using Unreal.Core.Attributes;
using Unreal.Core.Models.Enums;

namespace FortniteReplayReader.Models.NetFieldExports.Builds;

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Wood/L1/PBWA_W1_RoofC.PBWA_W1_RoofC_C", minimalParseMode: ParseMode.Minimal)]
public class WoodRoof : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Wood/L1/PBWA_W1_RoofI.PBWA_W1_RoofI_C", minimalParseMode: ParseMode.Minimal)]
public class WoodRoofI : BaseBuild
{

}


[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Stone/L1/PBWA_S1_RoofC.PBWA_S1_RoofC_C", minimalParseMode: ParseMode.Minimal)]
public class StoneRoof : BaseBuild { }

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Metal/L1/PBWA_M1_RoofC.PBWA_M1_RoofC_C", minimalParseMode: ParseMode.Minimal)]
public class MetalRoof : BaseBuild { }