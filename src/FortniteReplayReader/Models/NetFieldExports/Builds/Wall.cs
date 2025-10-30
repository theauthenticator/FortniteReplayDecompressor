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