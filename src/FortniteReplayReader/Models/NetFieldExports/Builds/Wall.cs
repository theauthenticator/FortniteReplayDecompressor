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
    [NetFieldExport("BuildingReplacementType", RepLayoutCmdType.Enum)]
    public int? BuildingReplacementType { get; set; }

    [NetFieldExport("bMirrored", RepLayoutCmdType.PropertyBool)]
    public bool? bMirrored { get; set; }

    [NetFieldExport("ReplicatedDrawScale3D", RepLayoutCmdType.Property)]
    public FVector? ReplicatedDrawScale3D { get; set; }
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Wood/L1/PBWA_W1_Brace.PBWA_W1_Brace_C", minimalParseMode: ParseMode.Minimal)]
public class WoodBraceWall : BaseBuild
{
    [NetFieldExport("BuildingReplacementType", RepLayoutCmdType.Enum)]
    public int? BuildingReplacementType { get; set; }

    [NetFieldExport("bMirrored", RepLayoutCmdType.PropertyBool)]
    public bool? bMirrored { get; set; }

    [NetFieldExport("ReplicatedDrawScale3D", RepLayoutCmdType.Property)]
    public FVector? ReplicatedDrawScale3D { get; set; }
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Wood/L1/PBWA_W1_DoorSide.PBWA_W1_DoorSide_C", minimalParseMode: ParseMode.Minimal)]
public class WoodDoorSideWall : BaseBuild
{
    [NetFieldExport("BuildingReplacementType", RepLayoutCmdType.Enum)]
    public int? BuildingReplacementType { get; set; }

    [NetFieldExport("bDoorOpen", RepLayoutCmdType.PropertyBool)]
    public bool? bDoorOpen { get; set; }

    [NetFieldExport("DoorDesiredRotOffset", RepLayoutCmdType.PropertyRotator)]
    public FRotator DoorDesiredRotOffset { get; set; }

    [NetFieldExport("DoorOpenStyle", RepLayoutCmdType.Enum)]
    public int? DoorOpenStyle { get; set; }

    [NetFieldExport("bDoorCollisionDisabled", RepLayoutCmdType.PropertyBool)]
    public bool? bDoorCollisionDisabled { get; set; }

    [NetFieldExport("bFastOpenRequested", RepLayoutCmdType.PropertyBool)]
    public bool? bFastOpenRequested { get; set; }
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Wood/L1/PBWA_W1_DoorC.PBWA_W1_DoorC_C", minimalParseMode: ParseMode.Minimal)]
public class WoodDoorWall : BaseBuild
{
    [NetFieldExport("BuildingReplacementType", RepLayoutCmdType.Enum)]
    public int? BuildingReplacementType { get; set; }

    [NetFieldExport("bDoorOpen", RepLayoutCmdType.PropertyBool)]
    public bool? bDoorOpen { get; set; }

    [NetFieldExport("bDoorCollisionDisabled", RepLayoutCmdType.PropertyBool)]
    public bool? bDoorCollisionDisabled { get; set; }
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Wood/L1/PBWA_W1_WindowSide.PBWA_W1_WindowSide_C", minimalParseMode: ParseMode.Minimal)]
public class WoodWindowSideWall : BaseBuild
{
    [NetFieldExport("BuildingReplacementType", RepLayoutCmdType.Enum)]
    public int? BuildingReplacementType { get; set; }
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Wood/L1/PBWA_W1_WindowC.PBWA_W1_WindowC_C", minimalParseMode: ParseMode.Minimal)]
public class WoodWindowCenterWall : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Wood/L1/PBWA_W1_QuarterWallS.PBWA_W1_QuarterWallS_C", minimalParseMode: ParseMode.Minimal)]
public class WoodQuarterWall : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Wood/L1/PBWA_W1_HalfWallS.PBWA_W1_HalfWallS_C", minimalParseMode: ParseMode.Minimal)]
public class WoodHalfWall : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Wood/L1/PBWA_W1_RoofO.PBWA_W1_RoofO_C", minimalParseMode: ParseMode.Minimal)]
public class WoodRoofOctagonal : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Wood/L1/PBWA_W1_RoofS.PBWA_W1_RoofS_C", minimalParseMode: ParseMode.Minimal)]
public class WoodRoofSlope : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Wood/L1/PBWA_W1_RoofD.PBWA_W1_RoofD_C", minimalParseMode: ParseMode.Minimal)]
public class WoodRoofDome : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Wood/L1/PBWA_W1_RoofWall.PBWA_W1_RoofWall_C", minimalParseMode: ParseMode.Minimal)]
public class WoodRoofWall : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Wood/L1/PBWA_W1_ArchwayLargeSupport.PBWA_W1_ArchwayLargeSupport_C", minimalParseMode: ParseMode.Minimal)]
public class WoodArchwayLargeSupport : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Wood/L1/PBWA_W1_BalconyO.PBWA_W1_BalconyO_C", minimalParseMode: ParseMode.Minimal)]
public class WoodBalconyOuter : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Stone/L1/PBWA_S1_Solid.PBWA_S1_Solid_C", minimalParseMode: ParseMode.Minimal)]
public class StoneWall : BaseBuild
{
    [NetFieldExport("BuildingReplacementType", RepLayoutCmdType.Enum)]
    public int? BuildingReplacementType { get; set; }

    [NetFieldExport("ProxyGameplayCueDamageMagnitude", RepLayoutCmdType.Property)]
    public float? ProxyGameplayCueDamageMagnitude { get; set; }

    [NetFieldExport("bUnderRepair", RepLayoutCmdType.PropertyBool)]
    public bool? bUnderRepair { get; set; }
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Stone/L1/PBWA_S1_ArchwayLarge.PBWA_S1_ArchwayLarge_C", minimalParseMode: ParseMode.Minimal)]
public class StoneArchwayWall : BaseBuild
{
    [NetFieldExport("BuildingReplacementType", RepLayoutCmdType.Enum)]
    public int? BuildingReplacementType { get; set; }

    [NetFieldExport("ProxyGameplayCueDamageMagnitude", RepLayoutCmdType.Property)]
    public float? ProxyGameplayCueDamageMagnitude { get; set; }
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Stone/L1/PBWA_S1_Brace.PBWA_S1_Brace_C", minimalParseMode: ParseMode.Minimal)]
public class StoneBraceWall : BaseBuild
{
    [NetFieldExport("BuildingReplacementType", RepLayoutCmdType.Enum)]
    public int? BuildingReplacementType { get; set; }
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Stone/L1/PBWA_S1_DoorSide.PBWA_S1_DoorSide_C", minimalParseMode: ParseMode.Minimal)]
public class StoneDoorSideWall : BaseBuild
{
    [NetFieldExport("BuildingReplacementType", RepLayoutCmdType.Enum)]
    public int? BuildingReplacementType { get; set; }

    [NetFieldExport("bDoorOpen", RepLayoutCmdType.PropertyBool)]
    public bool? bDoorOpen { get; set; }

    [NetFieldExport("DoorDesiredRotOffset", RepLayoutCmdType.PropertyRotator)]
    public FRotator DoorDesiredRotOffset { get; set; }

    [NetFieldExport("bDoorCollisionDisabled", RepLayoutCmdType.PropertyBool)]
    public bool? bDoorCollisionDisabled { get; set; }
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Stone/L1/PBWA_S1_DoorC.PBWA_S1_DoorC_C", minimalParseMode: ParseMode.Minimal)]
public class StoneDoorWall : BaseBuild
{
    [NetFieldExport("BuildingReplacementType", RepLayoutCmdType.Enum)]
    public int? BuildingReplacementType { get; set; }
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Stone/L1/PBWA_S1_WindowSide.PBWA_S1_WindowSide_C", minimalParseMode: ParseMode.Minimal)]
public class StoneWindowSideWall : BaseBuild
{
    [NetFieldExport("BuildingReplacementType", RepLayoutCmdType.Enum)]
    public int? BuildingReplacementType { get; set; }
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Stone/L1/PBWA_S1_WindowsC.PBWA_S1_WindowsC_C", minimalParseMode: ParseMode.Minimal)]
public class StoneWindowsCenterWall : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Stone/L1/PBWA_S1_HalfWallS.PBWA_S1_HalfWallS_C", minimalParseMode: ParseMode.Minimal)]
public class StoneHalfWall : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Stone/L1/PBWA_S1_QuarterWallS.PBWA_S1_QuarterWallS_C", minimalParseMode: ParseMode.Minimal)]
public class StoneQuarterWall : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Stone/L1/PBWA_S1_RoofO.PBWA_S1_RoofO_C", minimalParseMode: ParseMode.Minimal)]
public class StoneRoofOctagonal : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Stone/L1/PBWA_S1_RoofS.PBWA_S1_RoofS_C", minimalParseMode: ParseMode.Minimal)]
public class StoneRoofSlope : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Stone/L1/PBWA_S1_RoofWall.PBWA_S1_RoofWall_C", minimalParseMode: ParseMode.Minimal)]
public class StoneRoofWall : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Stone/L1/PBWA_S1_RoofI.PBWA_S1_RoofI_C", minimalParseMode: ParseMode.Minimal)]
public class StoneRoofInterior : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Stone/L1/PBWA_S1_BalconyI.PBWA_S1_BalconyI_C", minimalParseMode: ParseMode.Minimal)]
public class StoneBalconyInterior : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Stone/L1/PBWA_S1_BalconyO.PBWA_S1_BalconyO_C", minimalParseMode: ParseMode.Minimal)]
public class StoneBalconyOuter : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Stone/L1/PBWA_S1_BalconyS.PBWA_S1_BalconyS_C", minimalParseMode: ParseMode.Minimal)]
public class StoneBalconySmall : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Stone/L1/PBWA_S1_ArchwayLargeSupport.PBWA_S1_ArchwayLargeSupport_C", minimalParseMode: ParseMode.Minimal)]
public class StoneArchwayLargeSupport : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Metal/L1/PBWA_M1_Solid.PBWA_M1_Solid_C", minimalParseMode: ParseMode.Minimal)]
public class MetalWall : BaseBuild
{
    [NetFieldExport("BuildingReplacementType", RepLayoutCmdType.Enum)]
    public int? BuildingReplacementType { get; set; }

    [NetFieldExport("ProxyGameplayCueDamageMagnitude", RepLayoutCmdType.Property)]
    public float? ProxyGameplayCueDamageMagnitude { get; set; }

    [NetFieldExport("bUnderRepair", RepLayoutCmdType.PropertyBool)]
    public bool? bUnderRepair { get; set; }
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Metal/L1/PBWA_M1_ArchwayLarge.PBWA_M1_ArchwayLarge_C", minimalParseMode: ParseMode.Minimal)]
public class MetalArchwayWall : BaseBuild
{
    [NetFieldExport("BuildingReplacementType", RepLayoutCmdType.Enum)]
    public int? BuildingReplacementType { get; set; }

    [NetFieldExport("ProxyGameplayCueDamageMagnitude", RepLayoutCmdType.Property)]
    public float? ProxyGameplayCueDamageMagnitude { get; set; }
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Metal/L1/PBWA_M1_Brace.PBWA_M1_Brace_C", minimalParseMode: ParseMode.Minimal)]
public class MetalBraceWall : BaseBuild
{
    [NetFieldExport("BuildingReplacementType", RepLayoutCmdType.Enum)]
    public int? BuildingReplacementType { get; set; }
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Metal/L1/PBWA_M1_DoorSide.PBWA_M1_DoorSide_C", minimalParseMode: ParseMode.Minimal)]
public class MetalDoorSideWall : BaseBuild
{
    [NetFieldExport("BuildingReplacementType", RepLayoutCmdType.Enum)]
    public int? BuildingReplacementType { get; set; }

    [NetFieldExport("bDoorOpen", RepLayoutCmdType.PropertyBool)]
    public bool? bDoorOpen { get; set; }

    [NetFieldExport("DoorDesiredRotOffset", RepLayoutCmdType.PropertyRotator)]
    public FRotator DoorDesiredRotOffset { get; set; }

    [NetFieldExport("bDoorCollisionDisabled", RepLayoutCmdType.PropertyBool)]
    public bool? bDoorCollisionDisabled { get; set; }
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Metal/L1/PBWA_M1_DoorC.PBWA_M1_DoorC_C", minimalParseMode: ParseMode.Minimal)]
public class MetalDoorWall : BaseBuild
{
    [NetFieldExport("BuildingReplacementType", RepLayoutCmdType.Enum)]
    public int? BuildingReplacementType { get; set; }
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Metal/L1/PBWA_M1_WindowSide.PBWA_M1_WindowSide_C", minimalParseMode: ParseMode.Minimal)]
public class MetalWindowSideWall : BaseBuild
{
    [NetFieldExport("BuildingReplacementType", RepLayoutCmdType.Enum)]
    public int? BuildingReplacementType { get; set; }
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Metal/L1/PBWA_M1_HalfWall.PBWA_M1_HalfWall_C", minimalParseMode: ParseMode.Minimal)]
public class MetalHalfWall : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Metal/L1/PBWA_M1_QuarterWallS.PBWA_M1_QuarterWallS_C", minimalParseMode: ParseMode.Minimal)]
public class MetalQuarterWall : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Metal/L1/PBWA_M1_RoofO.PBWA_M1_RoofO_C", minimalParseMode: ParseMode.Minimal)]
public class MetalRoofOctagonal : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Metal/L1/PBWA_M1_RoofI.PBWA_M1_RoofI_C", minimalParseMode: ParseMode.Minimal)]
public class MetalRoofInterior : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Metal/L1/PBWA_M1_RoofS.PBWA_M1_RoofS_C", minimalParseMode: ParseMode.Minimal)]
public class MetalRoofSlope : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Metal/L1/PBWA_M1_RoofD.PBWA_M1_RoofD_C", minimalParseMode: ParseMode.Minimal)]
public class MetalRoofDome : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Metal/L1/PBWA_M1_RoofWall.PBWA_M1_RoofWall_C", minimalParseMode: ParseMode.Minimal)]
public class MetalRoofWall : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Metal/L1/PBWA_M1_Archway.PBWA_M1_Archway_C", minimalParseMode: ParseMode.Minimal)]
public class MetalArchway : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Metal/L1/PBWA_M1_BalconyI.PBWA_M1_BalconyI_C", minimalParseMode: ParseMode.Minimal)]
public class MetalBalconyInterior : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Metal/L1/PBWA_M1_BalconyS.PBWA_M1_BalconyS_C", minimalParseMode: ParseMode.Minimal)]
public class MetalBalconySmall : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Metal/L1/PBWA_M1_BalconyO.PBWA_M1_BalconyO_C", minimalParseMode: ParseMode.Minimal)]
public class MetalBalconyOuter : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Stone/L1/PBWA_S1_WindowsSide.PBWA_S1_WindowsSide_C", minimalParseMode: ParseMode.Minimal)]
public class StoneWindowsSideWall : BaseBuild
{
    [NetFieldExport("BuildingReplacementType", RepLayoutCmdType.Enum)]
    public int? BuildingReplacementType { get; set; }
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Metal/L1/PBWA_M1_ArchwayLargeSupport.PBWA_M1_ArchwayLargeSupport_C", minimalParseMode: ParseMode.Minimal)]
public class MetalArchwayLargeSupport : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Metal/L1/PBWA_M1_WindowC.PBWA_M1_WindowC_C", minimalParseMode: ParseMode.Minimal)]
public class MetalWindowCenterWall : BaseBuild
{
}


[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Stone/L1/PBWA_S1_HalfWallHalf.PBWA_S1_HalfWallHalf_C", minimalParseMode: ParseMode.Minimal)]
public class StoneHalfWallHalf : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Stone/L1/PBWA_S1_HalfWallDoor.PBWA_S1_HalfWallDoor_C", minimalParseMode: ParseMode.Minimal)]
public class StoneHalfWallDoor : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Stone/L1/PBWA_S1_HalfWallDoorSide.PBWA_S1_HalfWallDoorSide_C", minimalParseMode: ParseMode.Minimal)]
public class StoneHalfWallDoorSide : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Metal/L1/PBWA_M1_QuarterWallHalf.PBWA_M1_QuarterWallHalf_C", minimalParseMode: ParseMode.Minimal)]
public class MetalQuarterWallHalf : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Metal/L1/PBWA_M1_HalfWallDoorS.PBWA_M1_HalfWallDoorS_C", minimalParseMode: ParseMode.Minimal)]
public class MetalHalfWallDoorSide : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Metal/L1/PBWA_M1_HalfWallHalf.PBWA_M1_HalfWallHalf_C", minimalParseMode: ParseMode.Minimal)]
public class MetalHalfWallHalf : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Metal/L1/PBWA_M1_HalfWallDoor.PBWA_M1_HalfWallDoor_C", minimalParseMode: ParseMode.Minimal)]
public class MetalHalfWallDoor : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Stone/L1/PBWA_S1_BalconyD.PBWA_S1_BalconyD_C", minimalParseMode: ParseMode.Minimal)]
public class StoneBalconyDome : BaseBuild
{
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Player/Metal/L1/PBWA_M1_BalconyD.PBWA_M1_BalconyD_C", minimalParseMode: ParseMode.Minimal)]
public class MetalBalconyDome : BaseBuild
{
}