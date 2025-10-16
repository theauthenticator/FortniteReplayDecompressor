using Unreal.Core.Attributes;
using Unreal.Core.Models;
using Unreal.Core.Models.Enums;

namespace FortniteReplayReader.Models.NetFieldExports.Vehicles;

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Wood/L1/PBWA_W1_Solid.PBWA_W1_Solid_C", minimalParseMode: ParseMode.Minimal)]
public class WoodWall : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Wood/L1/PBWA_W1_ArchwayLarge.PBWA_W1_ArchwayLarge_C", minimalParseMode: ParseMode.Minimal)]
public class WoodArchwayWall : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Wood/L1/PBWA_W1_Brace.PBWA_W1_Brace_C", minimalParseMode: ParseMode.Minimal)]
public class WoodBraceWall : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Wood/L1/PBWA_W1_DoorSide.PBWA_W1_DoorSide_C", minimalParseMode: ParseMode.Minimal)]
public class WoodDoorSideWall : BaseBuild
{
    [NetFieldExport("bDoorOpen", RepLayoutCmdType.PropertyBool)]
    public bool? bDoorOpen { get; set; }

    [NetFieldExport("DoorDesiredRotOffset", RepLayoutCmdType.PropertyRotator)]
    public FRotator DoorDesiredRotOffset { get; set; }
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Wood/L1/PBWA_W1_DoorC.PBWA_W1_DoorC_C", minimalParseMode: ParseMode.Minimal)]
public class WoodDoorWall : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Wood/L1/PBWA_W1_WindowSide.PBWA_W1_WindowSide_C", minimalParseMode: ParseMode.Minimal)]
public class WoodWindowSideWall : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Stone/L1/PBWA_S1_Solid.PBWA_S1_Solid_C", minimalParseMode: ParseMode.Minimal)]
public class StoneWall : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Stone/L1/PBWA_S1_ArchwayLarge.PBWA_S1_ArchwayLarge_C", minimalParseMode: ParseMode.Minimal)]
public class StoneArchwayWall : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Stone/L1/PBWA_S1_Brace.PBWA_S1_Brace_C", minimalParseMode: ParseMode.Minimal)]
public class StoneBraceWall : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Stone/L1/PBWA_S1_DoorSide.PBWA_S1_DoorSide_C", minimalParseMode: ParseMode.Minimal)]
public class StoneDoorSideWall : BaseBuild
{
    [NetFieldExport("bDoorOpen", RepLayoutCmdType.PropertyBool)]
    public bool? bDoorOpen { get; set; }

    [NetFieldExport("DoorDesiredRotOffset", RepLayoutCmdType.PropertyRotator)]
    public FRotator DoorDesiredRotOffset { get; set; }
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Stone/L1/PBWA_S1_DoorC.PBWA_S1_DoorC_C", minimalParseMode: ParseMode.Minimal)]
public class StoneDoorWall : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Stone/L1/PBWA_S1_WindowSide.PBWA_S1_WindowSide_C", minimalParseMode: ParseMode.Minimal)]
public class StoneWindowSideWall : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Metal/L1/PBWA_M1_Solid.PBWA_M1_Solid_C", minimalParseMode: ParseMode.Minimal)]
public class MetalWall : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Metal/L1/PBWA_M1_ArchwayLarge.PBWA_M1_ArchwayLarge_C", minimalParseMode: ParseMode.Minimal)]
public class MetalArchwayWall : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Metal/L1/PBWA_M1_Brace.PBWA_M1_Brace_C", minimalParseMode: ParseMode.Minimal)]
public class MetalBraceWall : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Metal/L1/PBWA_M1_DoorSide.PBWA_M1_DoorSide_C", minimalParseMode: ParseMode.Minimal)]
public class MetalDoorSideWall : BaseBuild
{
    [NetFieldExport("bDoorOpen", RepLayoutCmdType.PropertyBool)]
    public bool? bDoorOpen { get; set; }

    [NetFieldExport("DoorDesiredRotOffset", RepLayoutCmdType.PropertyRotator)]
    public FRotator DoorDesiredRotOffset { get; set; }
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Metal/L1/PBWA_M1_DoorC.PBWA_M1_DoorC_C", minimalParseMode: ParseMode.Minimal)]
public class MetalDoorWall : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Metal/L1/PBWA_M1_WindowSide.PBWA_M1_WindowSide_C", minimalParseMode: ParseMode.Minimal)]
public class MetalWindowSideWall : BaseBuild
{
}