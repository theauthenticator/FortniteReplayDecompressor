using FortniteReplayReader.Models.NetFieldExports.Vehicles;
using Unreal.Core.Attributes;
using Unreal.Core.Models.Enums;

namespace FortniteReplayReader.Models.NetFieldExports.Builds;

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Wood/L1/PBWA_W1_Floor.PBWA_W1_Floor_C", minimalParseMode: ParseMode.Minimal)]
public class WoodFloor : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Wood/L1/PBWA_W1_BalconyI.PBWA_W1_BalconyI_C", minimalParseMode: ParseMode.Minimal)]
public class WoodBalconyIFloor : BaseBuild
{

}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Wood/L1/PBWA_W1_BalconyS.PBWA_W1_BalconyS_C", minimalParseMode: ParseMode.Minimal)]
public class WoodBalconySFloor : BaseBuild
{
}


[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Stone/L1/PBWA_S1_Floor.PBWA_S1_Floor_C", minimalParseMode: ParseMode.Minimal)]
public class StoneFloor : BaseBuild { }

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Metal/L1/PBWA_M1_Floor.PBWA_M1_Floor_C", minimalParseMode: ParseMode.Minimal)]
public class MetalFloor : BaseBuild { }